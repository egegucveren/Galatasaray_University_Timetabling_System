import { useState } from "react";
import {
  createEnrollment,
  getCoursesForSemester,
  getMyStudentGrades,
  getStudentElectives,
  getStudentEnrollment,
  getStudentSchedule,
  getTimeSlots,
  loginStudent,
} from "@/lib/api";
import type { Course, Enrollment, ScheduleResult, Student, StudentGrade, TimeSlot } from "@/lib/types";
import { getRequiredElectiveCount } from "@/lib/electiveRules";
import Timetable from "./Timetable";

const SEMESTER_LABELS = [
  "", "1. Yarıyıl", "2. Yarıyıl", "3. Yarıyıl", "4. Yarıyıl",
  "5. Yarıyıl", "6. Yarıyıl", "7. Yarıyıl", "8. Yarıyıl",
];

type Panel = "enroll" | "pending" | "schedule";
type RightTab = "schedule" | "grades";

export default function StudentPanel() {
  const [number, setNumber] = useState("");
  const [password, setPassword] = useState("");
  const [student, setStudent] = useState<Student | null>(null);
  const [enrollment, setEnrollment] = useState<Enrollment | null>(null);
  const [panel, setPanel] = useState<Panel>("enroll");

  // Enrollment form state
  const [selectedSemester, setSelectedSemester] = useState(0);
  const [semesterCourses, setSemesterCourses] = useState<Course[]>([]);
  const [chosenElectives, setChosenElectives] = useState<Set<number>>(new Set());
  const [loadingCourses, setLoadingCourses] = useState(false);

  // Schedule
  const [timeSlots, setTimeSlots] = useState<TimeSlot[]>([]);
  const [schedule, setSchedule] = useState<ScheduleResult[]>([]);

  // Transkript (kendi notlarım)
  const [grades, setGrades] = useState<StudentGrade[]>([]);
  const [rightTab, setRightTab] = useState<RightTab>("schedule");

  const [status, setStatus] = useState("Giriş yapınız.");
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  function handleLogout() {
    setStudent(null);
    setEnrollment(null);
    setPanel("enroll");
    setSelectedSemester(0);
    setSemesterCourses([]);
    setChosenElectives(new Set());
    setSchedule([]);
    setGrades([]);
    setRightTab("schedule");
    setError("");
    setStatus("Giriş yapınız.");
  }

  async function tryGetGrades(studentId: number, token: string): Promise<StudentGrade[]> {
    try { return await getMyStudentGrades(studentId, token); } catch { return []; }
  }

  async function handleLogin() {
    setError("");
    setStatus("Giriş kontrol ediliyor…");
    try {
      const s = await loginStudent(number, password);
      setStudent(s);

      const slots = await getTimeSlots();
      setTimeSlots(slots);

      // Check existing enrollment
      let enr: Enrollment | null = null;
      try {
        enr = await getStudentEnrollment(s.Id, s.Token);
      } catch {
        // no enrollment yet
      }
      setEnrollment(enr);

      const g = await tryGetGrades(s.Id, s.Token);
      setGrades(g);

      if (enr?.Status === "approved") {
        const sched = await tryGetSchedule(s.Id, s.Token);
        setSchedule(sched);
        setPanel("schedule");
        setStatus(`${s.Name} · ${SEMESTER_LABELS[enr.Semester]} · Onaylı`);
      } else if (enr?.Status === "pending") {
        setPanel("pending");
        setStatus(`${s.Name} · Onay bekleniyor`);
      } else {
        setPanel("enroll");
        setStatus(`${s.Name} · Kayıt yapılmadı`);
        await loadCoursesForSemester(s.CurrentSemester);
      }
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Hata oluştu.");
      setStatus("Giriş yapınız.");
    }
  }

  async function tryGetSchedule(studentId: number, token: string): Promise<ScheduleResult[]> {
    try { return await getStudentSchedule(studentId, token); } catch { return []; }
  }

  // Öğrenci yalnızca kendi CurrentSemester'ı için kayıt yapabilir — serbest yarıyıl
  // seçimi yok. Bu fonksiyon yalnızca o yarıyılın derslerini yükler.
  async function loadCoursesForSemester(sem: number) {
    setSelectedSemester(sem);
    setChosenElectives(new Set());
    setLoadingCourses(true);
    try {
      const courses = await getCoursesForSemester(sem);
      setSemesterCourses(courses);
    } catch {
      setSemesterCourses([]);
    } finally {
      setLoadingCourses(false);
    }
  }

  async function handleSubmitEnrollment() {
    if (!student || selectedSemester === 0) return;
    setSubmitting(true);
    setError("");
    try {
      const enr = await createEnrollment(student.Id, selectedSemester, [...chosenElectives], student.Token);
      setEnrollment(enr);
      setPanel("pending");
      setStatus(`${student.Name} · Onay bekleniyor`);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Kayıt gönderilemedi.");
    } finally {
      setSubmitting(false);
    }
  }

  async function handleChangeEnrollment() {
    if (!student || !enrollment) return;
    setError("");
    setPanel("enroll");
    setSelectedSemester(student.CurrentSemester);
    setChosenElectives(new Set());
    setLoadingCourses(true);
    try {
      const [courses, options] = await Promise.all([
        getCoursesForSemester(student.CurrentSemester),
        getStudentElectives(student.Id, student.Token).catch(() => []),
      ]);
      setSemesterCourses(courses);
      const courseIds = new Set(courses.map((c) => c.Id));
      const preSelected = new Set(
        options.filter((o) => o.IsSelected && courseIds.has(o.CourseId)).map((o) => o.CourseId)
      );
      setChosenElectives(preSelected);
    } catch {
      setSemesterCourses([]);
    } finally {
      setLoadingCourses(false);
    }
  }

  const mandatory = semesterCourses.filter((c) => !c.IsElective);
  const electives = semesterCourses.filter((c) => c.IsElective);

  // Group electives by elective_group
  const electiveGroups = electives.reduce<Record<string, Course[]>>((acc, c) => {
    (acc[c.ElectiveGroup] ??= []).push(c);
    return acc;
  }, {});

  // Her grup için (örn. "Teknik-6'dan 2 ders") tam olarak kaç ders seçilmesi gerektiği —
  // sunucudaki resmi kurala (ects.gsu.edu.tr) karşılık gelir. Limit dolunca ekstra kutu
  // işaretlenemez; asıl doğrulama yine de sunucu tarafında yapılır.
  function toggleElective(id: number, group: string) {
    setChosenElectives((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
        return next;
      }
      const groupIds = new Set((electiveGroups[group] ?? []).map((c) => c.Id));
      const chosenInGroup = [...prev].filter((cid) => groupIds.has(cid)).length;
      const limit = getRequiredElectiveCount(selectedSemester, group);
      if (chosenInGroup >= limit) return prev;
      next.add(id);
      return next;
    });
  }

  const allGroupsSatisfied = Object.entries(electiveGroups).every(([group, courses]) => {
    const required = getRequiredElectiveCount(selectedSemester, group);
    const chosen = courses.filter((c) => chosenElectives.has(c.Id)).length;
    return chosen === required;
  });

  return (
    <div className="grid grid-cols-[300px_1fr] gap-5">
      {/* Left panel */}
      <div className="card p-5 self-start min-h-[340px]">
        <h2 className="section-heading">Öğrenci Girişi</h2>

        {!student ? (
          <>
            <label className="field-label">Öğrenci Numarası</label>
            <input className="field-input mb-3" value={number} onChange={(e) => setNumber(e.target.value)} />
            <label className="field-label">Şifre</label>
            <input className="field-input mb-5" type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
            <button className="btn btn-primary w-full" onClick={handleLogin}>Giriş Yap</button>
            <p className="text-xs text-muted mt-3">Demo: 2022001 / 19/07/2004</p>
            {error && <p className="text-primary text-xs mt-2">{error}</p>}
          </>
        ) : (
          <>
            {/* User card */}
            <div className="flex items-center gap-2 mb-5 p-3 bg-bg-page rounded-lg">
              <div className="w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center text-primary font-bold text-sm">
                {student.Name[0]}
              </div>
              <div className="flex-1 min-w-0">
                <p className="text-sm font-semibold text-ink truncate">{student.Name}</p>
                <p className="text-xs text-muted">{student.StudentNumber}</p>
              </div>
              <button className="btn btn-secondary text-xs py-1 px-2 shrink-0" onClick={handleLogout}>Çıkış</button>
            </div>

            {/* Enrollment form */}
            {panel === "enroll" && (
              <>
                <h2 className="section-heading">Yarıyıl Kaydı</h2>
                <div className="field-input mb-4 flex items-center justify-between bg-bg-page">
                  <span className="text-sm font-semibold text-ink">{SEMESTER_LABELS[selectedSemester]}</span>
                  <span className="text-[0.65rem] text-muted uppercase tracking-wide">Bulunduğunuz yarıyıl</span>
                </div>

                {loadingCourses && <p className="text-xs text-muted mb-3">Dersler yükleniyor…</p>}

                {semesterCourses.length > 0 && (
                  <>
                    {/* Mandatory */}
                    <p className="text-xs font-semibold text-muted uppercase tracking-wide mb-2">
                      Zorunlu Dersler ({mandatory.length})
                    </p>
                    <div className="flex flex-col gap-1 mb-4 max-h-40 overflow-y-auto">
                      {mandatory.map((c) => (
                        <div key={c.Id} className="flex items-center gap-2 px-3 py-2 bg-bg-page rounded-lg">
                          <div className="w-3 h-3 rounded-sm bg-primary/40 shrink-0" />
                          <span className="text-xs text-ink">{c.Title}</span>
                        </div>
                      ))}
                    </div>

                    {/* Electives by group */}
                    {Object.entries(electiveGroups).map(([group, courses]) => {
                      const required = getRequiredElectiveCount(selectedSemester, group);
                      const chosenCount = courses.filter((c) => chosenElectives.has(c.Id)).length;
                      return (
                        <div key={group} className="mb-3">
                          <p className="text-xs font-semibold text-muted uppercase tracking-wide mb-2 flex items-center justify-between">
                            <span>Seçmeli — {group}</span>
                            <span className={chosenCount === required ? "text-green-700" : "text-primary"}>
                              {chosenCount}/{required} seçildi
                            </span>
                          </p>
                          <div className="flex flex-col gap-1">
                            {courses.map((c) => {
                              const checked = chosenElectives.has(c.Id);
                              const disabled = !checked && chosenCount >= required;
                              return (
                                <label
                                  key={c.Id}
                                  className={`flex items-start gap-2 px-3 py-2 border border-line rounded-lg bg-bg-page transition-colors ${disabled ? "opacity-50 cursor-not-allowed" : "cursor-pointer hover:border-primary/40"}`}
                                >
                                  <input
                                    type="checkbox"
                                    className="mt-0.5 accent-primary shrink-0"
                                    checked={checked}
                                    disabled={disabled}
                                    onChange={() => toggleElective(c.Id, group)}
                                  />
                                  <span className="text-xs text-ink leading-tight">{c.Title}</span>
                                </label>
                              );
                            })}
                          </div>
                        </div>
                      );
                    })}

                    <button
                      className="btn btn-primary w-full mt-2"
                      onClick={handleSubmitEnrollment}
                      disabled={submitting || !allGroupsSatisfied}
                    >
                      {submitting ? "Gönderiliyor…" : "Kayıt Talebini Gönder"}
                    </button>
                    {!allGroupsSatisfied && (
                      <p className="text-[0.65rem] text-muted mt-1">Devam etmek için her seçmeli gruptan gereken sayıda ders seçin.</p>
                    )}
                  </>
                )}

                {error && <p className="text-primary text-xs mt-2">{error}</p>}
              </>
            )}

            {/* Pending state */}
            {panel === "pending" && enrollment && (
              <div className="text-center py-4">
                <div className="w-12 h-12 rounded-full bg-yellow-100 flex items-center justify-center mx-auto mb-3">
                  <span className="text-2xl">⏳</span>
                </div>
                <p className="text-sm font-semibold text-ink mb-1">Onay Bekleniyor</p>
                <p className="text-xs text-muted mb-3">{SEMESTER_LABELS[enrollment.Semester]}</p>
                {enrollment.ReviewerNote && (
                  <p className="text-xs text-red-600 bg-red-50 rounded px-3 py-2 mb-3">{enrollment.ReviewerNote}</p>
                )}
                <button
                  className="btn btn-secondary w-full text-xs"
                  onClick={handleChangeEnrollment}
                >
                  Kaydı Değiştir
                </button>
              </div>
            )}

            {/* Approved — show tabs */}
            {panel === "schedule" && enrollment && (
              <div>
                <div className="flex items-center gap-2 mb-3 p-2 bg-green-50 border border-green-200 rounded-lg">
                  <span className="text-green-600 text-sm">✓</span>
                  <div>
                    <p className="text-xs font-semibold text-green-800">{SEMESTER_LABELS[enrollment.Semester]}</p>
                    <p className="text-xs text-green-700">Kayıt onaylandı</p>
                  </div>
                </div>
                <p className="text-xs text-muted mb-3">
                  {schedule.length > 0 ? `${schedule.length} ders programda görünüyor.` : "Program henüz oluşturulmadı."}
                </p>
                <button
                  className="btn btn-secondary w-full text-xs"
                  onClick={handleChangeEnrollment}
                >
                  Yarıyıl / Seçmeli Dersleri Güncelle
                </button>
                <p className="text-xs text-muted mt-2">
                  Değişiklik yaptığınızda kaydınız tekrar admin onayına düşer.
                </p>
              </div>
            )}
          </>
        )}
      </div>

      {/* Right panel */}
      <div className="card">
        <div className="status-bar">{status}</div>

        {student && (
          <div className="flex gap-1 px-5 pt-4 pb-0 border-b border-line">
            {(["schedule", "grades"] as RightTab[]).map((tab) => (
              <button
                key={tab}
                onClick={() => setRightTab(tab)}
                className={`px-4 py-1.5 text-sm font-semibold rounded-t-lg border border-b-0 transition-colors ${
                  rightTab === tab
                    ? "bg-white text-primary border-line -mb-px"
                    : "bg-bg-page text-muted border-transparent hover:text-ink"
                }`}
              >
                {tab === "schedule" ? "Program" : "Notlarım"}
              </button>
            ))}
          </div>
        )}

        <div className="p-5">
          {rightTab === "grades" && student ? (
            <StudentTranscript grades={grades} />
          ) : panel === "schedule" ? (
            <Timetable items={schedule} timeSlots={timeSlots} mode="student" printTitle={`GSÜ ${student?.Name} - ${enrollment ? SEMESTER_LABELS[enrollment.Semester] : ""} Ders Programı`} />
          ) : (
            <div className="flex items-center justify-center h-48 text-muted text-sm">
              {panel === "pending"
                ? "Kayıt admin tarafından onaylandıktan sonra programınız görünecek."
                : panel === "enroll" && student
                  ? "Yarıyıl seçip kayıt talebini gönderin."
                  : "Giriş yapınız."}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function gradeColor(passed: boolean) {
  return passed ? "bg-green-100 text-green-700" : "bg-red-100 text-red-700";
}

function StudentTranscript({ grades }: { grades: StudentGrade[] }) {
  if (grades.length === 0) {
    return <p className="text-sm text-muted italic">Henüz not girilmemiş.</p>;
  }

  const bySemester = grades.reduce<Record<number, StudentGrade[]>>((acc, g) => {
    (acc[g.CourseSemester] ??= []).push(g);
    return acc;
  }, {});

  const passedCount = grades.filter((g) => g.Passed).length;

  return (
    <div className="flex flex-col gap-5">
      <p className="text-xs text-muted">{passedCount}/{grades.length} ders geçildi.</p>
      {Object.entries(bySemester)
        .sort(([a], [b]) => Number(a) - Number(b))
        .map(([sem, semGrades]) => (
          <div key={sem}>
            <h4 className="text-xs font-semibold text-muted uppercase tracking-wide mb-2">
              {SEMESTER_LABELS[Number(sem)] || `${sem}. Yarıyıl`}
            </h4>
            <div className="rounded-lg border border-line overflow-hidden">
              <table className="w-full text-sm border-collapse">
                <thead>
                  <tr className="bg-gray-50 text-xs text-muted">
                    <th className="text-left px-3 py-2">Ders</th>
                    <th className="text-center px-3 py-2 w-16">Not</th>
                    <th className="text-center px-3 py-2 w-20">Durum</th>
                  </tr>
                </thead>
                <tbody>
                  {semGrades.map((g) => (
                    <tr key={g.Id} className="border-t border-line">
                      <td className="px-3 py-2 text-ink">{g.CourseTitle}</td>
                      <td className="px-3 py-2 text-center font-bold">{g.Grade}</td>
                      <td className="px-3 py-2 text-center">
                        <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${gradeColor(g.Passed)}`}>
                          {g.Passed ? "Geçti" : "Kaldı"}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        ))}
    </div>
  );
}
