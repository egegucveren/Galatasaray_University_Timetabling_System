import { useState } from "react";
import {
  addPrerequisite,
  deletePrerequisite,
  getCourseCatalog,
  getTeacherAvailability,
  getTeacherCourses,
  getTeacherPrerequisites,
  getTeacherSchedule,
  getTeacherStudents,
  getTimeSlots,
  loginTeacher,
  saveTeacherAvailability,
  upsertTeacherGrade,
} from "@/lib/api";
import type { Course, CoursePrerequisite, ScheduleResult, Teacher, TeacherStudentCourse, TimeSlot } from "@/lib/types";
import Timetable from "./Timetable";

type Tab = "schedule" | "availability" | "grades" | "prereqs";

const GRADE_OPTIONS = ["AA", "BA", "BB", "CB", "CC", "DC", "DD", "FF"];
const PASSED_GRADES = new Set(["AA", "BA", "BB", "CB", "CC", "DC", "DD"]);

const DAYS = ["Pazartesi", "Sali", "Carsamba", "Persembe", "Cuma"];
const DAY_LABELS: Record<string, string> = {
  Pazartesi: "Pazartesi", Sali: "Salı", Carsamba: "Çarşamba", Persembe: "Perşembe", Cuma: "Cuma",
};

function gradeColor(passed: boolean | null, grade: string | null) {
  if (!grade) return "bg-gray-100 text-gray-500";
  if (passed) return "bg-green-100 text-green-700";
  return "bg-red-100 text-red-700";
}

function minutesFrom(clock: string) {
  const [h, m] = clock.split(":").map(Number);
  return h * 60 + m;
}

// Brief'in istediği gibi: öğretmenin müsait olmadığı saatleri düz bir liste yerine
// gerçek bir haftalık çizelge (gün × saat ızgarası) üzerinde işaretlemesini sağlar.
// Her hücre, o gün/saate karşılık gelen tek bir TimeSlot'u temsil eder (tüm zaman
// dilimleri seed verisinde tam 1 saatlik olduğundan çoklu-saat birleştirmeye gerek yok).
function AvailabilityGrid({
  timeSlots,
  unavailable,
  onToggle,
}: {
  timeSlots: TimeSlot[];
  unavailable: Set<number>;
  onToggle: (slotId: number) => void;
}) {
  if (!timeSlots.length) {
    return <p className="text-sm text-muted italic p-2">Zaman dilimleri yükleniyor…</p>;
  }

  const knownDays = new Set(timeSlots.map((s) => s.Day));
  const days = DAYS.filter((d) => knownDays.has(d));

  const hourRanges = [...new Set(timeSlots.map((s) => s.HourRange))].sort(
    (a, b) => minutesFrom(a.split("-")[0].trim()) - minutesFrom(b.split("-")[0].trim())
  );

  const slotByDayHour = new Map<string, TimeSlot>();
  for (const slot of timeSlots) {
    slotByDayHour.set(`${slot.Day}__${slot.HourRange}`, slot);
  }

  return (
    <div className="overflow-x-auto rounded-lg border border-line">
      <table className="w-full border-collapse min-w-[560px] bg-white text-sm table-fixed">
        <thead>
          <tr>
            <th className="border-b border-r border-line px-2 py-3 bg-gray-50 text-xs font-semibold text-muted uppercase tracking-wider w-20 text-center">
              Saat
            </th>
            {days.map((day) => (
              <th key={day} className="border-b border-r border-line px-3 py-3 bg-gray-50 text-xs font-semibold text-ink uppercase tracking-wider text-center last:border-r-0">
                {DAY_LABELS[day] ?? day}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {hourRanges.map((hourRange, rowIdx) => (
            <tr key={hourRange} className={rowIdx % 2 === 0 ? "bg-white" : "bg-gray-50/40"}>
              <td className="border-b border-r border-line px-1 py-2 text-center text-[0.68rem] font-semibold text-muted whitespace-nowrap align-middle">
                {hourRange}
              </td>
              {days.map((day) => {
                const slot = slotByDayHour.get(`${day}__${hourRange}`);
                if (!slot) {
                  return <td key={day} className="border-b border-r border-line last:border-r-0" />;
                }
                const isUnavailable = unavailable.has(slot.Id);
                return (
                  <td key={day} className="border-b border-r border-line last:border-r-0 p-1.5 align-middle">
                    <button
                      type="button"
                      onClick={() => onToggle(slot.Id)}
                      className={`w-full h-12 rounded-lg text-xs font-semibold transition-colors ${
                        isUnavailable
                          ? "bg-red-100 text-red-700 border border-red-200 hover:bg-red-200"
                          : "bg-green-50 text-green-700 border border-green-200 hover:bg-green-100"
                      }`}
                    >
                      {isUnavailable ? "Müsait Değil" : "Müsait"}
                    </button>
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default function TeacherPanel() {
  const [teacherNumberInput, setTeacherNumberInput] = useState("");
  const [password, setPassword] = useState("");
  const [teacher, setTeacher]   = useState<Teacher | null>(null);
  const [timeSlots, setTimeSlots]   = useState<TimeSlot[]>([]);
  const [schedule, setSchedule]     = useState<ScheduleResult[]>([]);
  const [unavailable, setUnavailable] = useState<Set<number>>(new Set());
  const [status, setStatus] = useState("Giriş yapınız.");
  const [error, setError]   = useState("");
  const [activeTab, setActiveTab] = useState<Tab>("schedule");

  // Notlar
  const [studentCourses, setStudentCourses] = useState<TeacherStudentCourse[]>([]);
  const [gradeInputs, setGradeInputs] = useState<Record<string, string>>({});
  const [gradeSaving, setGradeSaving] = useState<Record<string, boolean>>({});
  const [gradeError, setGradeError]   = useState("");

  // Ön koşullar
  const [ownCourses, setOwnCourses] = useState<Course[]>([]);
  const [courseCatalog, setCourseCatalog] = useState<Course[]>([]);
  const [prereqs, setPrereqs] = useState<CoursePrerequisite[]>([]);
  const [prereqCourseId, setPrereqCourseId] = useState(0);
  const [prereqTargetId, setPrereqTargetId] = useState(0);
  const [prereqGroup, setPrereqGroup] = useState(1);
  const [prereqError, setPrereqError] = useState("");
  const [prereqSaving, setPrereqSaving] = useState(false);

  function handleLogout() {
    setTeacher(null);
    setTimeSlots([]);
    setSchedule([]);
    setUnavailable(new Set());
    setStudentCourses([]);
    setGradeInputs({});
    setOwnCourses([]);
    setCourseCatalog([]);
    setPrereqs([]);
    setPrereqCourseId(0);
    setPrereqTargetId(0);
    setPrereqGroup(1);
    setPrereqError("");
    setError("");
    setStatus("Giriş yapınız.");
    setActiveTab("schedule");
  }

  async function handleLogin() {
    setError("");
    if (!teacherNumberInput.trim()) {
      setError("Öğretmen numaranızı girin.");
      return;
    }
    setStatus("Giriş kontrol ediliyor…");
    try {
      const t = await loginTeacher(teacherNumberInput.trim(), password);
      setTeacher(t);
      const [slots, avail] = await Promise.all([
        getTimeSlots(),
        getTeacherAvailability(t.Id, t.Token),
      ]);
      setTimeSlots(slots);
      setUnavailable(new Set(avail.UnavailabilitySlots));
      let sched: ScheduleResult[] = [];
      try { sched = await getTeacherSchedule(t.Id, t.Token); } catch { /* henüz program yok */ }
      setSchedule(sched);
      setStatus(`${t.Name} · ${sched.length} ders`);
      // Load students
      const sc = await getTeacherStudents(t.Id, t.Token);
      setStudentCourses(sc);
      // Pre-fill grade inputs with existing grades
      const init: Record<string, string> = {};
      sc.forEach((s) => { if (s.Grade) init[`${s.StudentId}-${s.CourseId}`] = s.Grade; });
      setGradeInputs(init);

      // Load own courses + full catalog + existing prerequisites
      const [own, catalog, existingPrereqs] = await Promise.all([
        getTeacherCourses(t.Id, t.Token),
        getCourseCatalog(),
        getTeacherPrerequisites(t.Id, t.Token),
      ]);
      setOwnCourses(own);
      setCourseCatalog(catalog);
      setPrereqs(existingPrereqs);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Hata oluştu.");
      setStatus("Giriş yapınız.");
    }
  }

  async function handleSaveAvailability() {
    if (!teacher) return;
    try {
      await saveTeacherAvailability(teacher.Id, [...unavailable], teacher.Token);
      const sched = await getTeacherSchedule(teacher.Id, teacher.Token);
      setSchedule(sched);
      setStatus(`${teacher.Name} · ${sched.length} ders`);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Hata oluştu.");
    }
  }

  async function handleSaveGrade(studentId: number, courseId: number) {
    if (!teacher) return;
    const key   = `${studentId}-${courseId}`;
    const grade = gradeInputs[key];
    if (!grade) return;
    const passed = PASSED_GRADES.has(grade);
    setGradeSaving((p) => ({ ...p, [key]: true }));
    setGradeError("");
    try {
      await upsertTeacherGrade(studentId, courseId, grade, passed, teacher.Token);
      setStudentCourses((prev) =>
        prev.map((sc) =>
          sc.StudentId === studentId && sc.CourseId === courseId
            ? { ...sc, Grade: grade, Passed: passed }
            : sc
        )
      );
    } catch (e: unknown) {
      setGradeError(e instanceof Error ? e.message : "Kaydetme hatası.");
    } finally {
      setGradeSaving((p) => ({ ...p, [key]: false }));
    }
  }

  async function handleAddPrerequisite() {
    if (!teacher) return;
    setPrereqError("");
    if (!prereqCourseId) { setPrereqError("Önce kendi dersinizi seçin."); return; }
    if (!prereqTargetId) { setPrereqError("Ön koşul dersini seçin."); return; }
    if (prereqCourseId === prereqTargetId) { setPrereqError("Bir ders kendi ön koşulu olamaz."); return; }

    setPrereqSaving(true);
    try {
      const created = await addPrerequisite(teacher.Id, prereqCourseId, prereqTargetId, prereqGroup, teacher.Token);
      setPrereqs((prev) => [...prev, created]);
      setPrereqTargetId(0);
    } catch (e: unknown) {
      setPrereqError(e instanceof Error ? e.message : "Ön koşul eklenemedi.");
    } finally {
      setPrereqSaving(false);
    }
  }

  async function handleDeletePrerequisite(id: number) {
    if (!teacher) return;
    try {
      await deletePrerequisite(teacher.Id, id, teacher.Token);
      setPrereqs((prev) => prev.filter((p) => p.Id !== id));
    } catch (e: unknown) {
      setPrereqError(e instanceof Error ? e.message : "Ön koşul silinemedi.");
    }
  }

  function toggleSlot(slotId: number) {
    setUnavailable((prev) => {
      const next = new Set(prev);
      next.has(slotId) ? next.delete(slotId) : next.add(slotId);
      return next;
    });
  }

  // Group studentCourses by course for the Notlar tab
  const byCourse = new Map<number, { title: string; semester: number; students: TeacherStudentCourse[] }>();
  for (const sc of studentCourses) {
    if (!byCourse.has(sc.CourseId)) {
      byCourse.set(sc.CourseId, { title: sc.CourseTitle, semester: sc.CourseSemester, students: [] });
    }
    byCourse.get(sc.CourseId)!.students.push(sc);
  }
  const courseEntries = [...byCourse.entries()].sort((a, b) => a[1].semester - b[1].semester || a[1].title.localeCompare(b[1].title));

  // Group prerequisites by course for the Ön Koşullar tab
  const prereqsByCourse = new Map<number, { title: string; items: CoursePrerequisite[] }>();
  for (const p of prereqs) {
    if (!prereqsByCourse.has(p.CourseId)) {
      prereqsByCourse.set(p.CourseId, { title: p.CourseTitle, items: [] });
    }
    prereqsByCourse.get(p.CourseId)!.items.push(p);
  }
  const prereqEntries = [...prereqsByCourse.entries()].sort((a, b) => a[1].title.localeCompare(b[1].title));
  const catalogOptions = courseCatalog
    .filter((c) => c.Id !== prereqCourseId)
    .sort((a, b) => a.Semester - b.Semester || a.Title.localeCompare(b.Title));

  return (
    <div className="grid grid-cols-[300px_1fr] gap-5">
      {/* Left sidebar */}
      <div className="card p-5 self-start min-h-[340px]">
        <h2 className="section-heading">Öğretmen Girişi</h2>

        {!teacher ? (
          <>
            <label className="field-label">Öğretmen Numarası</label>
            <input
              className="field-input mb-3"
              type="text"
              inputMode="numeric"
              autoComplete="username"
              value={teacherNumberInput}
              onChange={(e) => setTeacherNumberInput(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") handleLogin(); }}
            />
            <label className="field-label">Şifre</label>
            <input
              className="field-input mb-5"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") handleLogin(); }}
            />
            <button className="btn btn-primary w-full" onClick={handleLogin}>Giriş Yap</button>
            <p className="text-xs text-muted mt-3">Demo: 1001 / 1234</p>
            {error && <p className="text-primary text-xs mt-2">{error}</p>}
          </>
        ) : (
          <>
            <div className="flex items-center gap-2 mb-5 p-3 bg-bg-page rounded-lg">
              <div className="w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center text-primary font-bold text-sm">
                {teacher.Name[0]}
              </div>
              <div className="flex-1 min-w-0">
                <p className="text-sm font-semibold text-ink truncate">{teacher.Name}</p>
                <p className="text-xs text-muted truncate">No: {teacher.TeacherNumber} · {teacher.Email}</p>
              </div>
              <button className="btn btn-secondary text-xs py-1 px-2 shrink-0" onClick={handleLogout}>Çıkış</button>
            </div>

            <p className="text-sm text-muted">
              Müsait olmadığınız saatleri sağdaki <strong className="text-ink">Müsaitlik</strong> sekmesindeki
              haftalık çizelge üzerinden işaretleyebilirsiniz.
            </p>
            {error && <p className="text-primary text-xs mt-2">{error}</p>}
          </>
        )}
      </div>

      {/* Right panel */}
      <div className="card">
        <div className="status-bar">{status}</div>

        {teacher && (
          <div className="flex gap-1 px-5 pt-4 pb-0 border-b border-line">
            {(["schedule", "availability", "grades", "prereqs"] as Tab[]).map((tab) => (
              <button
                key={tab}
                onClick={() => setActiveTab(tab)}
                className={`px-4 py-1.5 text-sm font-semibold rounded-t-lg border border-b-0 transition-colors ${
                  activeTab === tab
                    ? "bg-white text-primary border-line -mb-px"
                    : "bg-bg-page text-muted border-transparent hover:text-ink"
                }`}
              >
                {tab === "schedule" ? "Program" : tab === "availability" ? "Müsaitlik" : tab === "grades" ? "Notlar" : "Ön Koşullar"}
              </button>
            ))}
          </div>
        )}

        <div className="p-5">
          {!teacher ? (
            <div className="flex items-center justify-center h-48 text-muted text-sm">
              Giriş yapınız.
            </div>
          ) : activeTab === "schedule" ? (
            <Timetable
              items={schedule}
              timeSlots={timeSlots}
              mode="teacher"
              printTitle={`GSÜ ${teacher?.Name ?? ""} - Ders Programı`}
            />
          ) : activeTab === "availability" ? (
            /* ── Müsaitlik tab: haftalık çizelge üzerinde işaretleme ── */
            <div>
              <p className="text-sm text-muted mb-4">
                Müsait olmadığınız hücrelere tıklayarak işaretleyin, ardından kaydedin.
                Program otomatik oluşturulurken bu saatlerde size ders atanmaz.
              </p>
              <AvailabilityGrid timeSlots={timeSlots} unavailable={unavailable} onToggle={toggleSlot} />
              <button className="btn btn-primary mt-4" onClick={handleSaveAvailability}>
                Müsaitliği Kaydet
              </button>
              {error && <p className="text-primary text-xs mt-2">{error}</p>}
            </div>
          ) : activeTab === "grades" ? (
            /* ── Notlar tab ── */
            <div>
              {gradeError && (
                <p className="text-primary text-sm mb-3 p-2 bg-red-50 rounded">{gradeError}</p>
              )}
              {courseEntries.length === 0 ? (
                <p className="text-sm text-muted italic">Henüz öğrenci kaydı yok.</p>
              ) : (
                <div className="flex flex-col gap-5">
                  {courseEntries.map(([courseId, { title, semester, students }]) => {
                    const totalGraded = students.filter((s) => s.Grade).length;
                    return (
                      <div key={courseId} className="border border-line rounded-xl overflow-hidden">
                        {/* Course header */}
                        <div className="flex items-center justify-between px-4 py-3 bg-gray-50 border-b border-line">
                          <div>
                            <span className="font-semibold text-ink text-sm">{title}</span>
                            <span className="ml-2 text-xs text-muted">{semester}. Yarıyıl</span>
                          </div>
                          <span className="text-xs text-muted">{totalGraded}/{students.length} notlandırıldı</span>
                        </div>

                        {/* Student rows */}
                        <div className="divide-y divide-line">
                          {students.map((sc) => {
                            const key = `${sc.StudentId}-${sc.CourseId}`;
                            const inputGrade = gradeInputs[key] ?? "";
                            const saving = gradeSaving[key] ?? false;
                            const dirty = inputGrade !== (sc.Grade ?? "");
                            return (
                              <div key={sc.StudentId} className="flex items-center gap-3 px-4 py-2.5">
                                {/* Student info */}
                                <div className="flex-1 min-w-0">
                                  <p className="text-sm font-medium text-ink truncate">{sc.StudentName}</p>
                                  <p className="text-xs text-muted">{sc.StudentNumber}</p>
                                </div>

                                {/* Current grade badge */}
                                <span className={`text-xs font-bold px-2 py-0.5 rounded-full ${gradeColor(sc.Passed, sc.Grade)}`}>
                                  {sc.Grade ?? "—"}
                                </span>

                                {/* Grade select */}
                                <select
                                  className="field-input py-1 text-sm w-20"
                                  value={inputGrade}
                                  onChange={(e) =>
                                    setGradeInputs((p) => ({ ...p, [key]: e.target.value }))
                                  }
                                >
                                  <option value="">Not</option>
                                  {GRADE_OPTIONS.map((g) => (
                                    <option key={g} value={g}>{g}</option>
                                  ))}
                                </select>

                                {/* Save button */}
                                <button
                                  className={`btn text-xs py-1 px-3 ${dirty && inputGrade ? "btn-primary" : "btn-secondary opacity-50"}`}
                                  disabled={!dirty || !inputGrade || saving}
                                  onClick={() => handleSaveGrade(sc.StudentId, sc.CourseId)}
                                >
                                  {saving ? "…" : "Kaydet"}
                                </button>
                              </div>
                            );
                          })}
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          ) : (
            /* ── Ön Koşullar tab ── */
            <div>
              {prereqError && (
                <p className="text-primary text-sm mb-3 p-2 bg-red-50 rounded">{prereqError}</p>
              )}

              {ownCourses.length === 0 ? (
                <p className="text-sm text-muted italic">Adınıza tanımlı bir ders bulunamadı.</p>
              ) : (
                <>
                  {/* Ekleme formu */}
                  <div className="bg-gray-50 rounded-xl p-4 mb-5 border border-line">
                    <h3 className="text-sm font-semibold mb-3">Ön Koşul Ekle</h3>
                    <div className="flex gap-2 flex-wrap items-end">
                      <div className="flex flex-col gap-1">
                        <label className="text-xs text-muted">Dersiniz</label>
                        <select
                          className="field-input text-xs py-1.5 min-w-48"
                          value={prereqCourseId}
                          onChange={(e) => setPrereqCourseId(Number(e.target.value))}
                        >
                          <option value={0}>— Ders seçin —</option>
                          {ownCourses.map((c) => (
                            <option key={c.Id} value={c.Id}>{c.Semester}. Yarıyıl — {c.Title}</option>
                          ))}
                        </select>
                      </div>
                      <div className="flex flex-col gap-1">
                        <label className="text-xs text-muted">Ön Koşul Dersi</label>
                        <select
                          className="field-input text-xs py-1.5 min-w-48"
                          value={prereqTargetId}
                          onChange={(e) => setPrereqTargetId(Number(e.target.value))}
                        >
                          <option value={0}>— Ders seçin —</option>
                          {catalogOptions.map((c) => (
                            <option key={c.Id} value={c.Id}>{c.Semester}. Yarıyıl — {c.Title}</option>
                          ))}
                        </select>
                      </div>
                      <div className="flex flex-col gap-1">
                        <label className="text-xs text-muted" title="Aynı grup numarasına sahip ön koşullardan biri yeterlidir (VEYA mantığı).">
                          Grup No (VEYA)
                        </label>
                        <input
                          type="number"
                          min={1}
                          className="field-input text-xs py-1.5 w-20"
                          value={prereqGroup}
                          onChange={(e) => setPrereqGroup(Math.max(1, Number(e.target.value)))}
                        />
                      </div>
                      <button
                        className="btn btn-primary text-xs py-1.5 px-3"
                        disabled={prereqSaving}
                        onClick={handleAddPrerequisite}
                      >
                        {prereqSaving ? "Ekleniyor…" : "Ekle"}
                      </button>
                    </div>
                    <p className="text-xs text-muted mt-2">
                      Aynı grup numarasını verdiğiniz ön koşullardan yalnızca biri geçilmiş olması yeterlidir (VEYA).
                      Farklı grup numaraları verirseniz her grup ayrı ayrı sağlanmalıdır (VE).
                    </p>
                  </div>

                  {/* Mevcut ön koşullar listesi */}
                  {prereqEntries.length === 0 ? (
                    <p className="text-sm text-muted italic">Henüz tanımlı ön koşul yok.</p>
                  ) : (
                    <div className="flex flex-col gap-4">
                      {prereqEntries.map(([courseId, { title, items }]) => (
                        <div key={courseId} className="border border-line rounded-xl overflow-hidden">
                          <div className="px-4 py-3 bg-gray-50 border-b border-line">
                            <span className="font-semibold text-ink text-sm">{title}</span>
                          </div>
                          <div className="divide-y divide-line">
                            {items.map((p) => (
                              <div key={p.Id} className="flex items-center justify-between px-4 py-2.5">
                                <div>
                                  <span className="text-sm text-ink">{p.PrerequisiteCourseTitle}</span>
                                  <span className="ml-2 text-xs text-muted">Grup {p.PrereqGroup}</span>
                                </div>
                                <button
                                  className="btn btn-danger text-xs py-1 px-3"
                                  onClick={() => handleDeletePrerequisite(p.Id)}
                                >
                                  Sil
                                </button>
                              </div>
                            ))}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
