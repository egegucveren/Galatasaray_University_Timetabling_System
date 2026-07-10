import { useState } from "react";
import {
  addCourse,
  adjustSchedule,
  advanceStudentsSemester,
  checkPrerequisites,
  deleteCourse,
  deleteGrade,
  generateSchedule,
  getAdminSchedule,
  getAdminStudents,
  getAllEnrollments,
  getCourses,
  getRooms,
  getStudentGrades,
  getTeachers,
  getTimeSlots,
  loginAdmin,
  logoutAdmin,
  registerStudent,
  resetSchedule,
  reviewEnrollment,
  upsertGrade,
} from "@/lib/api";
import type { Admin, Course, Enrollment, PrerequisiteCheckResult, Room, ScheduleResult, SessionDraft, Student, StudentGrade, Teacher, TimeSlot } from "@/lib/types";
import Timetable from "./Timetable";

const DAYS = ["Pazartesi", "Sali", "Carsamba", "Persembe", "Cuma"];

function getStartTime(slot: TimeSlot) {
  return slot.HourRange.split("-")[0].trim();
}

export default function AdminPanel() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [admin, setAdmin] = useState<Admin | null>(null);
  const [view, setView] = useState<"schedule" | "courses" | "enrollments" | "students">("schedule");

  const [timeSlots, setTimeSlots] = useState<TimeSlot[]>([]);
  const [rooms, setRooms] = useState<Room[]>([]);
  const [courses, setCourses] = useState<Course[]>([]);
  const [teachers, setTeachers] = useState<Teacher[]>([]);
  const [schedule, setSchedule] = useState<ScheduleResult[]>([]);
  const [enrollments, setEnrollments] = useState<Enrollment[]>([]);
  const [reviewNote, setReviewNote] = useState<Record<number, string>>({});
  const [semesterFilter, setSemesterFilter] = useState<number>(0); // 0 = tümü

  // Öğrenci & not state
  const [students, setStudents] = useState<Student[]>([]);
  const [selectedStudentId, setSelectedStudentId] = useState<number>(0);
  const [advanceSelection, setAdvanceSelection] = useState<Set<number>>(new Set());
  const [advancing, setAdvancing] = useState(false);
  const [studentGrades, setStudentGrades] = useState<StudentGrade[]>([]);
  const [expandedEnrollmentIds, setExpandedEnrollmentIds] = useState<Set<number>>(new Set());
  const [enrollmentGrades, setEnrollmentGrades] = useState<Record<number, StudentGrade[]>>({}); // keyed by studentId
  const [enrollmentPrereqs, setEnrollmentPrereqs] = useState<Record<number, PrerequisiteCheckResult[]>>({}); // keyed by enrollmentId
  const [gradeForm, setGradeForm] = useState<{ courseId: number; grade: string; passed: boolean }>({ courseId: 0, grade: "AA", passed: true });

  // Yeni öğrenci kaydı state
  const [newStudentName, setNewStudentName] = useState("");
  const [newStudentNumber, setNewStudentNumber] = useState("");
  const [newStudentBirthDate, setNewStudentBirthDate] = useState("");
  const [registerSaving, setRegisterSaving] = useState(false);
  const [registerError, setRegisterError] = useState("");
  const [registerResult, setRegisterResult] = useState<{ email: string; password: string } | null>(null);

  const [status, setStatus] = useState("Giriş yapınız.");
  const [error, setError] = useState("");

  // Manuel düzenleme state
  const [selectedCourseId, setSelectedCourseId] = useState<number>(0);
  const [selectedDay, setSelectedDay] = useState("");
  const [selectedStartTime, setSelectedStartTime] = useState("");
  const [selectedDuration, setSelectedDuration] = useState<number>(1);
  const [selectedRoomId, setSelectedRoomId] = useState<number>(0);
  const [drafts, setDrafts] = useState<SessionDraft[]>([]);
  const [activeDraftIdx, setActiveDraftIdx] = useState(-1);

  // Ders ekleme formu state
  const [newTitle, setNewTitle] = useState("");
  const [newTeacherId, setNewTeacherId] = useState<number>(0);
  const [newWeeklyHours, setNewWeeklyHours] = useState<number>(2);
  const [newSemester, setNewSemester] = useState<number>(0);
  const [newIsElective, setNewIsElective] = useState(false);
  const [newElectiveGroup, setNewElectiveGroup] = useState("");
  const [newExpectedCount, setNewExpectedCount] = useState<number>(30);
  const [addError, setAddError] = useState("");

  async function handleLogin() {
    setError("");
    setStatus("Giriş kontrol ediliyor…");
    try {
      const a = await loginAdmin(email, password);
      setAdmin(a);
      await loadContext(a.Token);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Hata oluştu.");
      setStatus("Giriş yapınız.");
    }
  }

  async function loadContext(token: string) {
    setStatus("Veriler yükleniyor…");
    try {
      const [slots, roomData, courseData, teacherData, sched, enrData, studentData] = await Promise.all([
        getTimeSlots(),
        getRooms(token),
        getCourses(token),
        getTeachers(token),
        getAdminSchedule(token),
        getAllEnrollments(token),
        getAdminStudents(token),
      ]);
      setTimeSlots(slots);
      setRooms(roomData);
      setCourses(courseData);
      setTeachers(teacherData);
      setSchedule(sched);
      setEnrollments(enrData);
      setStudents(studentData);
      const pending = enrData.filter((e) => e.Status === "pending").length;
      setStatus(
        sched.length
          ? `${courseData.length} ders · ${sched.length} ders programa yerleşti${pending ? ` · ${pending} bekleyen kayıt` : ""}`
          : `${courseData.length} ders yüklendi · Program henüz oluşturulmadı${pending ? ` · ${pending} bekleyen kayıt` : ""}`
      );
    } catch (e: unknown) {
      setStatus("Yükleme başarısız: " + (e instanceof Error ? e.message : "Hata"));
    }
  }

  function toggleAdvanceSelection(studentId: number) {
    setAdvanceSelection((prev) => {
      const next = new Set(prev);
      next.has(studentId) ? next.delete(studentId) : next.add(studentId);
      return next;
    });
  }

  async function handleAdvanceSemester() {
    if (!admin || advanceSelection.size === 0) return;
    setAdvancing(true);
    try {
      const updated = await advanceStudentsSemester(admin.Token, [...advanceSelection]);
      const updatedById = new Map(updated.map((s) => [s.Id, s]));
      setStudents((prev) => prev.map((s) => updatedById.get(s.Id) ?? s));
      setAdvanceSelection(new Set());
    } catch (e: unknown) {
      setStatus("İlerletme başarısız: " + (e instanceof Error ? e.message : "Hata"));
    } finally {
      setAdvancing(false);
    }
  }

  async function handleToggleTranscript(studentId: number, enrollmentId: number, semester: number) {
    if (expandedEnrollmentIds.has(enrollmentId)) {
      setExpandedEnrollmentIds((prev) => { const s = new Set(prev); s.delete(enrollmentId); return s; });
      return;
    }
    if (!admin) return;
    try {
      const [grades, prereqs] = await Promise.all([
        getStudentGrades(admin.Token, studentId),
        checkPrerequisites(studentId, semester, admin.Token),
      ]);
      setEnrollmentGrades((prev) => ({ ...prev, [studentId]: grades }));
      setEnrollmentPrereqs((prev) => ({ ...prev, [enrollmentId]: prereqs }));
      setExpandedEnrollmentIds((prev) => new Set(prev).add(enrollmentId));
    } catch { /* ignore */ }
  }

  async function handleLoadStudentGrades(studentId: number) {
    if (!admin || !studentId) return;
    try {
      const grades = await getStudentGrades(admin.Token, studentId);
      setStudentGrades(grades);
    } catch { /* ignore */ }
  }

  async function handleRegisterStudent() {
    if (!admin) return;
    setRegisterError("");
    setRegisterResult(null);
    if (!newStudentName.trim()) { setRegisterError("Ad soyad boş olamaz."); return; }
    if (!newStudentNumber.trim()) { setRegisterError("Öğrenci numarası boş olamaz."); return; }
    if (!newStudentBirthDate) { setRegisterError("Doğum tarihi girilmeli."); return; }

    setRegisterSaving(true);
    try {
      const { Student: created, GeneratedPassword } = await registerStudent(admin.Token, {
        name: newStudentName.trim(),
        studentNumber: newStudentNumber.trim(),
        birthDate: newStudentBirthDate,
      });
      setStudents((prev) => [...prev, created].sort((a, b) => a.Name.localeCompare(b.Name)));
      setRegisterResult({ email: created.Email, password: GeneratedPassword });
      setNewStudentName(""); setNewStudentNumber(""); setNewStudentBirthDate("");
    } catch (e: unknown) {
      setRegisterError(e instanceof Error ? e.message : "Kayıt oluşturulamadı.");
    } finally {
      setRegisterSaving(false);
    }
  }

  async function handleUpsertGrade() {
    if (!admin || !selectedStudentId || !gradeForm.courseId) return;
    // Geçti/Kaldı durumu her zaman harf notundan türetilir; admin tarafında
    // bundan bağımsız bir "Durum" seçilememeli (FF'nin Geçti olarak
    // işaretlenmesini ve transkript/ön koşul kontrolüyle tutarsızlığı önler).
    const passed = gradeForm.grade !== "FF";
    try {
      const grade = await upsertGrade(admin.Token, selectedStudentId, gradeForm.courseId, gradeForm.grade, passed);
      setStudentGrades((prev) => {
        const idx = prev.findIndex((g) => g.CourseId === grade.CourseId);
        return idx >= 0 ? prev.map((g, i) => i === idx ? grade : g) : [...prev, grade];
      });
    } catch (e) { alert(e instanceof Error ? e.message : "Hata"); }
  }

  async function handleDeleteGrade(gradeId: number) {
    if (!admin) return;
    try {
      await deleteGrade(admin.Token, gradeId);
      setStudentGrades((prev) => prev.filter((g) => g.Id !== gradeId));
    } catch (e) { alert(e instanceof Error ? e.message : "Hata"); }
  }

  async function handleReview(id: number, status: "approved" | "rejected") {
    if (!admin) return;
    const note = reviewNote[id] ?? "";
    try {
      const updated = await reviewEnrollment(admin.Token, id, status, note);
      setEnrollments((prev) => prev.map((e) => (e.Id === id ? updated : e)));
      setReviewNote((prev) => { const n = { ...prev }; delete n[id]; return n; });
    } catch (e: unknown) { setStatus(e instanceof Error ? e.message : "Hata oluştu."); }
  }

  async function handleLogout() {
    if (!admin) return;
    try { await logoutAdmin(admin.Token); } catch { /* ignore */ }
    setAdmin(null); setSchedule([]); setCourses([]); setRooms([]); setTimeSlots([]); setTeachers([]); setDrafts([]);
    setStatus("Giriş yapınız.");
  }

  const [generateSemester, setGenerateSemester] = useState(0);

  async function handleGenerate() {
    if (!admin) return;
    const label = generateSemester ? `${generateSemester}. Yarıyıl` : "tüm yarıyıllar";
    setStatus(`${label} için program oluşturuluyor…`);
    try {
      const sched = await generateSchedule(admin.Token, generateSemester);
      setSchedule(sched);
      setStatus(`${label} programı oluşturuldu · toplam ${sched.length} ders`);
    } catch (e: unknown) { setStatus(e instanceof Error ? e.message : "Hata oluştu."); }
  }

  async function handleReset() {
    if (!admin || !confirm("Tüm program ve manuel yerleştirmeler silinecek. Emin misiniz?")) return;
    try {
      await resetSchedule(admin.Token);
      setSchedule([]); setDrafts([]);
      setStatus("Program sıfırlandı.");
    } catch (e: unknown) { setStatus(e instanceof Error ? e.message : "Hata oluştu."); }
  }

  // ── Manuel düzenleme ──

  function onCourseChange(courseId: number) {
    setSelectedCourseId(courseId);
    if (!courseId) { setDrafts([]); return; }
    const existing = schedule.filter((e) => e.CourseId === courseId);
    if (existing.length) {
      const newDrafts: SessionDraft[] = existing.map((e) => {
        const slot = timeSlots.find((s) => s.Id === e.TimeSlotId);
        return { roomId: e.RoomId, durationHours: e.DurationHours, day: slot?.Day ?? e.Day, startTime: slot ? getStartTime(slot) : e.HourRange.split("-")[0].trim(), isManualSource: e.IsManual };
      });
      setDrafts(newDrafts); setActiveDraftIdx(0); loadDraftIntoControls(newDrafts[0]);
    } else {
      const course = courses.find((c) => c.Id === courseId);
      setDrafts([{ roomId: 0, durationHours: course?.WeeklyHours ?? 1, day: "", startTime: "", isManualSource: false }]);
      setActiveDraftIdx(0);
    }
  }

  function loadDraftIntoControls(d: SessionDraft) {
    setSelectedDay(d.day); setSelectedStartTime(d.startTime);
    setSelectedDuration(d.durationHours); setSelectedRoomId(d.roomId);
  }

  function upsertDraft() {
    const draft: SessionDraft = { roomId: selectedRoomId, durationHours: selectedDuration, day: selectedDay, startTime: selectedStartTime, isManualSource: true };
    if (activeDraftIdx >= 0 && activeDraftIdx < drafts.length) {
      const next = [...drafts]; next[activeDraftIdx] = draft; setDrafts(next);
    } else { setDrafts([...drafts, draft]); setActiveDraftIdx(drafts.length); }
    setStatus("Oturum listesi güncellendi.");
  }

  function removeDraft() {
    if (activeDraftIdx < 0) return;
    const next = drafts.filter((_, i) => i !== activeDraftIdx);
    setDrafts(next); setActiveDraftIdx(next.length ? 0 : -1);
    if (next.length) loadDraftIntoControls(next[0]);
  }

  async function handleSaveManual() {
    if (!admin || !selectedCourseId) return;
    let liveDrafts = drafts;
    if (selectedDay && selectedStartTime && selectedRoomId) {
      const draft: SessionDraft = { roomId: selectedRoomId, durationHours: selectedDuration, day: selectedDay, startTime: selectedStartTime, isManualSource: true };
      if (activeDraftIdx >= 0 && activeDraftIdx < liveDrafts.length) {
        liveDrafts = [...liveDrafts]; liveDrafts[activeDraftIdx] = draft;
      } else { liveDrafts = [...liveDrafts, draft]; }
      setDrafts(liveDrafts);
    }
    if (!liveDrafts.length) { setStatus("Önce bir oturum ekleyin."); return; }
    const sessions = liveDrafts.map((d) => {
      const slot = timeSlots.find((s) => s.Day === d.day && getStartTime(s) === d.startTime);
      return { roomId: d.roomId, startTimeSlotId: slot?.Id ?? 0, durationHours: d.durationHours };
    });
    if (sessions.some((s) => !s.roomId || !s.startTimeSlotId || !s.durationHours)) {
      setStatus("Tüm oturumlar için gün, saat, süre ve oda seçimi tamamlanmalı."); return;
    }
    try {
      const sched = await adjustSchedule(admin.Token, { courseId: selectedCourseId, sessions });
      setSchedule(sched); setStatus("Manuel yerleştirme kaydedildi.");
    } catch (e: unknown) { setStatus(e instanceof Error ? e.message : "Hata oluştu."); }
  }

  async function handleClearManual() {
    if (!admin || !selectedCourseId) return;
    try {
      const sched = await adjustSchedule(admin.Token, { courseId: selectedCourseId, clearManualAssignment: true });
      setSchedule(sched); setDrafts([]); setStatus("Manuel yerleştirme kaldırıldı.");
    } catch (e: unknown) { setStatus(e instanceof Error ? e.message : "Hata oluştu."); }
  }

  // ── Ders CRUD ──

  async function handleAddCourse() {
    if (!admin) return;
    setAddError("");
    if (!newTitle.trim()) { setAddError("Ders adı boş olamaz."); return; }
    if (!newTeacherId) { setAddError("Öğretmen seçilmedi."); return; }
    if (!newSemester) { setAddError("Yarıyıl seçilmeli."); return; }

    try {
      const created = await addCourse(admin.Token, {
        title: newTitle.trim(),
        teacherId: newTeacherId,
        weeklyHours: newWeeklyHours,
        isElective: newIsElective,
        electiveGroup: newElectiveGroup.trim(),
        expectedStudentCount: newExpectedCount,
        semester: newSemester,
      });
      setCourses((prev) => [...prev, created]);
      // Formu temizle
      setNewTitle(""); setNewTeacherId(0); setNewWeeklyHours(2);
      setNewSemester(0); setNewIsElective(false); setNewElectiveGroup(""); setNewExpectedCount(30);
      setStatus(`"${created.Title}" eklendi.`);
    } catch (e: unknown) { setAddError(e instanceof Error ? e.message : "Hata oluştu."); }
  }

  async function handleDeleteCourse(id: number, title: string) {
    if (!admin || !confirm(`"${title}" dersi silinecek. Emin misiniz?`)) return;
    try {
      await deleteCourse(admin.Token, id);
      setCourses((prev) => prev.filter((c) => c.Id !== id));
      setSchedule((prev) => prev.filter((s) => s.CourseId !== id));
      setStatus(`"${title}" silindi.`);
    } catch (e: unknown) { setStatus(e instanceof Error ? e.message : "Hata oluştu."); }
  }

  const daySlots = timeSlots.filter((s) => s.Day === selectedDay).sort((a, b) => getStartTime(a).localeCompare(getStartTime(b)));
  const activeDays = DAYS.filter((d) => timeSlots.some((s) => s.Day === d));

  return (
    <div className="flex flex-col gap-5">
      {/* Action bar */}
      {admin && (
        <div className="card px-4 py-3 flex gap-2 flex-wrap items-center">
          <div className="flex items-center gap-2">
            <div className="select-wrap w-40">
              <select
                className="select-input"
                value={generateSemester}
                onChange={(e) => setGenerateSemester(Number(e.target.value))}
              >
                <option value={0}>Tüm Yarıyıllar</option>
                {[1,2,3,4,5,6,7,8].map((s) => <option key={s} value={s}>{s}. Yarıyıl</option>)}
              </select>
              <svg className="select-chevron" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                <path d="M6 9l6 6 6-6"/>
              </svg>
            </div>
            <button className="btn btn-primary" onClick={handleGenerate}>Otomatik Oluştur</button>
          </div>
          <button className="btn btn-secondary" onClick={() => loadContext(admin.Token)}>Yenile</button>
          <button className="btn btn-danger" onClick={handleReset}>Sıfırla</button>

          {/* Görünüm sekmeleri */}
          <div className="flex gap-1 ml-4 bg-gray-100 rounded-lg p-1">
            {(["schedule", "courses", "enrollments", "students"] as const).map((v) => {
              const label = v === "schedule" ? "Program" : v === "courses" ? "Dersler" : v === "enrollments" ? "Kayıtlar" : "Öğrenciler";
              const badge = v === "enrollments" ? enrollments.filter((e) => e.Status === "pending").length : 0;
              return (
                <button
                  key={v}
                  onClick={() => setView(v)}
                  className={`px-3 py-1 rounded-md text-xs font-semibold transition-all flex items-center gap-1 ${view === v ? "bg-white shadow-sm text-ink" : "text-muted hover:text-ink"}`}
                >
                  {label}
                  {badge > 0 && <span className="bg-primary text-white text-[0.6rem] w-4 h-4 rounded-full flex items-center justify-center">{badge}</span>}
                </button>
              );
            })}
          </div>

          <button className="btn btn-secondary ml-auto" onClick={handleLogout}>Çıkış Yap</button>
        </div>
      )}

      {/* Program görünümü */}
      {view === "schedule" && (
        <div className="grid grid-cols-[300px_1fr] gap-5">
          {/* Sidebar */}
          <div className="card p-5 self-start min-h-[340px]">
            {!admin ? (
              <>
                <h2 className="section-heading">Admin Girişi</h2>
                <label className="field-label">E-posta</label>
                <input className="field-input mb-3" value={email} onChange={(e) => setEmail(e.target.value)} />
                <label className="field-label">Şifre</label>
                <input className="field-input mb-5" type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
                <button className="btn btn-primary w-full" onClick={handleLogin}>Giriş Yap</button>
                <p className="text-xs text-muted mt-3">Sadece admin program oluşturabilir.</p>
                {error && <p className="text-primary text-xs mt-2">{error}</p>}
              </>
            ) : (
              <>
                <h2 className="section-heading">Manuel Düzenleme</h2>

                <label className="field-label">Ders</label>
                <select className="field-input mb-3" value={selectedCourseId} onChange={(e) => onCourseChange(Number(e.target.value))}>
                  <option value={0}>Ders seçin</option>
                  {courses.map((c) => <option key={c.Id} value={c.Id}>{c.Title} · {c.Semester}. Yarıyıl</option>)}
                </select>

                <label className="field-label">Gün</label>
                <select className="field-input mb-3" value={selectedDay} onChange={(e) => { setSelectedDay(e.target.value); setSelectedStartTime(""); }}>
                  <option value="">Gün seçin</option>
                  {activeDays.map((d) => <option key={d} value={d}>{d}</option>)}
                </select>

                <label className="field-label">Başlangıç Saati</label>
                <select className="field-input mb-3" value={selectedStartTime} onChange={(e) => setSelectedStartTime(e.target.value)}>
                  <option value="">Saat seçin</option>
                  {daySlots.map((s) => { const t = getStartTime(s); return <option key={s.Id} value={t}>{t}</option>; })}
                </select>

                <label className="field-label">Süre</label>
                <select className="field-input mb-3" value={selectedDuration} onChange={(e) => setSelectedDuration(Number(e.target.value))}>
                  {[1,2,3,4].map((h) => <option key={h} value={h}>{h} saat</option>)}
                </select>

                <label className="field-label">Oda</label>
                <select className="field-input mb-4" value={selectedRoomId} onChange={(e) => setSelectedRoomId(Number(e.target.value))}>
                  <option value={0}>Oda seçin</option>
                  {rooms.map((r) => <option key={r.Id} value={r.Id}>{r.Name}{r.IsAmphi ? " (Amfi)" : ""}</option>)}
                </select>

                <div className="flex gap-2 flex-wrap mb-4">
                  <button className="btn btn-primary text-xs py-1.5 px-3" onClick={upsertDraft}>Kaydet</button>
                  <button className="btn btn-secondary text-xs py-1.5 px-3" onClick={() => { setActiveDraftIdx(-1); setSelectedDay(""); setSelectedStartTime(""); setSelectedRoomId(0); }}>Yeni</button>
                  <button className="btn btn-secondary text-xs py-1.5 px-3" onClick={removeDraft}>Sil</button>
                </div>

                <div className="flex flex-col gap-1.5 mb-4">
                  {drafts.length === 0 && <p className="text-xs text-muted italic">Henüz oturum eklenmedi.</p>}
                  {drafts.map((d, i) => {
                    const room = rooms.find((r) => r.Id === d.roomId);
                    return (
                      <div key={i} onClick={() => { setActiveDraftIdx(i); loadDraftIntoControls(d); }}
                        className={`p-2.5 rounded-lg border text-xs cursor-pointer transition-colors ${i === activeDraftIdx ? "border-primary bg-primary/5 text-primary" : "border-line bg-bg-page text-ink hover:border-primary/30"}`}>
                        <span className="font-semibold">{d.day || "?"}</span> · {d.startTime || "?"} · {room?.Name ?? "?"} · {d.durationHours}h
                      </div>
                    );
                  })}
                </div>

                <div className="flex gap-2 flex-wrap">
                  <button className="btn btn-primary text-xs py-1.5 px-3" onClick={handleSaveManual}>Manuel Kaydet</button>
                  <button className="btn btn-secondary text-xs py-1.5 px-3" onClick={handleClearManual}>Temizle</button>
                </div>
              </>
            )}
          </div>

          {/* Timetable */}
          <div className="card">
            <div className="status-bar flex items-center justify-between">
              <span>{status}</span>
              <div className="select-wrap w-40">
                <select
                  className="select-input"
                  value={semesterFilter}
                  onChange={(e) => setSemesterFilter(Number(e.target.value))}
                >
                  <option value={0}>Tüm Yarıyıllar</option>
                  {[1,2,3,4,5,6,7,8].map((s) => (
                    <option key={s} value={s}>{s}. Yarıyıl</option>
                  ))}
                </select>
                <svg className="select-chevron" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                  <path d="M6 9l6 6 6-6"/>
                </svg>
              </div>
            </div>
            <div className="p-5">
              {!admin ? (
                <div className="flex items-center justify-center h-48 text-muted text-sm">
                  Giriş yapınız.
                </div>
              ) : (
                (() => {
                  const semCourseIds = semesterFilter
                    ? new Set(courses.filter((c) => c.Semester === semesterFilter).map((c) => c.Id))
                    : null;
                  const filtered = semCourseIds
                    ? schedule.filter((s) => semCourseIds.has(s.CourseId))
                    : schedule;
                  const semLabel = semesterFilter
                    ? `GSÜ Bilgisayar Mühendisliği ${semesterFilter}. Yarıyıl Ders Programı`
                    : "GSÜ Bilgisayar Mühendisliği Ders Programı";
                  return <Timetable items={filtered} timeSlots={timeSlots} mode="admin" printTitle={semLabel} />;
                })()
              )}
            </div>
          </div>
        </div>
      )}

      {/* Kayıtlar görünümü */}
      {view === "enrollments" && admin && (
        <div className="card">
          <div className="status-bar">
            {enrollments.filter((e) => e.Status === "pending").length} bekleyen · {" "}
            {enrollments.filter((e) => e.Status === "approved").length} onaylı · {" "}
            {enrollments.filter((e) => e.Status === "rejected").length} reddedildi
          </div>
          <div className="p-5">
            <h2 className="section-heading mb-4">Öğrenci Kayıt Talepleri ({enrollments.length})</h2>
            {enrollments.length === 0 ? (
              <p className="text-sm text-muted italic">Henüz kayıt talebi yok.</p>
            ) : (
              <div className="flex flex-col gap-3">
                {enrollments.map((enr) => {
                  const statusColor = enr.Status === "approved"
                    ? "border-green-200 bg-green-50"
                    : enr.Status === "rejected"
                      ? "border-red-200 bg-red-50"
                      : "border-yellow-200 bg-yellow-50";
                  const statusLabel = enr.Status === "approved" ? "Onaylı" : enr.Status === "rejected" ? "Reddedildi" : "Bekliyor";
                  const statusBadge = enr.Status === "approved"
                    ? "bg-green-100 text-green-800"
                    : enr.Status === "rejected"
                      ? "bg-red-100 text-red-800"
                      : "bg-yellow-100 text-yellow-800";
                  const semLabel = ["","1. Yarıyıl","2. Yarıyıl","3. Yarıyıl","4. Yarıyıl","5. Yarıyıl","6. Yarıyıl","7. Yarıyıl","8. Yarıyıl"][enr.Semester] ?? `${enr.Semester}. Yarıyıl`;
                  return (
                    <div key={enr.Id} className={`border rounded-xl p-4 ${statusColor}`}>
                      <div className="flex items-start justify-between gap-3 mb-2">
                        <div>
                          <p className="text-sm font-semibold text-ink">{enr.StudentName}</p>
                          <p className="text-xs text-muted">{enr.StudentNumber} · {semLabel}</p>
                          <p className="text-xs text-muted mt-0.5">
                            {new Date(enr.CreatedAt).toLocaleDateString("tr-TR", { day: "numeric", month: "long", year: "numeric", hour: "2-digit", minute: "2-digit" })}
                          </p>
                        </div>
                        <span className={`text-xs px-2 py-0.5 rounded-full font-medium shrink-0 ${statusBadge}`}>{statusLabel}</span>
                      </div>

                      {enr.ReviewerNote && (
                        <p className="text-xs text-muted italic mb-2">Not: {enr.ReviewerNote}</p>
                      )}

                      {/* Transkript aç/kapat */}
                      <button
                        className="text-xs text-primary underline mb-2"
                        onClick={() => handleToggleTranscript(enr.StudentId, enr.Id, enr.Semester)}
                      >
                        {expandedEnrollmentIds.has(enr.Id) ? "Transkripti Gizle" : "Transkripti Göster"}
                      </button>

                      {expandedEnrollmentIds.has(enr.Id) && (
                        <div className="mt-1 mb-3 bg-white rounded-lg border border-line p-3 space-y-3">
                          {/* Ön koşul uyarıları */}
                          {(enrollmentPrereqs[enr.Id] ?? []).length > 0 && (
                            <div className="bg-red-50 border border-red-200 rounded-lg p-2.5">
                              <p className="text-xs font-semibold text-red-700 mb-1">⚠ Eksik Ön Koşullar ({enr.Semester}. Yarıyıl)</p>
                              <ul className="text-xs text-red-600 space-y-0.5">
                                {(enrollmentPrereqs[enr.Id] ?? []).map((p) => (
                                  <li key={p.CourseId}>
                                    <span className="font-medium">{p.CourseTitle}:</span>{" "}
                                    {p.MissingPrerequisites.join(", ")} gerekli
                                  </li>
                                ))}
                              </ul>
                            </div>
                          )}
                          {(enrollmentPrereqs[enr.Id] ?? []).length === 0 && (
                            <p className="text-xs text-green-700 font-medium">✓ Tüm ön koşullar sağlanıyor ({enr.Semester}. Yarıyıl)</p>
                          )}

                          {/* Transkript tablosu */}
                          {(enrollmentGrades[enr.StudentId] ?? []).length === 0 ? (
                            <p className="text-xs text-muted italic">Kayıtlı not bulunamadı.</p>
                          ) : (
                            <table className="w-full text-xs border-collapse">
                              <thead>
                                <tr className="text-muted uppercase tracking-wide">
                                  <th className="text-left pb-1 pr-3">Ders</th>
                                  <th className="text-center pb-1 pr-2">Yarıyıl</th>
                                  <th className="text-center pb-1 pr-2">Not</th>
                                  <th className="text-center pb-1">Durum</th>
                                </tr>
                              </thead>
                              <tbody>
                                {(enrollmentGrades[enr.StudentId] ?? []).map((g) => (
                                  <tr key={g.Id} className="border-t border-line/50">
                                    <td className="py-1 pr-3 text-ink">{g.CourseTitle}</td>
                                    <td className="py-1 pr-2 text-center text-muted">{g.CourseSemester}. Yarıyıl</td>
                                    <td className="py-1 pr-2 text-center font-semibold">{g.Grade}</td>
                                    <td className="py-1 text-center">
                                      <span className={`px-1.5 py-0.5 rounded text-[0.6rem] font-medium ${g.Passed ? "bg-green-100 text-green-800" : "bg-red-100 text-red-800"}`}>
                                        {g.Passed ? "Geçti" : "Kaldı"}
                                      </span>
                                    </td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          )}
                        </div>
                      )}

                      {enr.Status === "pending" && (
                        <div className="mt-1 flex gap-2 items-center flex-wrap">
                          <input
                            className="field-input text-xs py-1.5 flex-1 min-w-32"
                            placeholder="Not ekle (isteğe bağlı)"
                            value={reviewNote[enr.Id] ?? ""}
                            onChange={(e) => setReviewNote((prev) => ({ ...prev, [enr.Id]: e.target.value }))}
                          />
                          <button className="btn btn-primary text-xs py-1.5 px-3" onClick={() => handleReview(enr.Id, "approved")}>Onayla</button>
                          <button className="btn btn-danger text-xs py-1.5 px-3" onClick={() => handleReview(enr.Id, "rejected")}>Reddet</button>
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        </div>
      )}

      {/* Öğrenciler görünümü */}
      {view === "students" && admin && (
        <div className="grid grid-cols-[280px_1fr] gap-5 items-start">
          {/* Öğrenci listesi */}
          <div className="card p-4">
            <h2 className="section-heading mb-3">Yeni Öğrenci Kaydet</h2>
            <div className="flex flex-col gap-2 mb-4 pb-4 border-b border-line">
              <input
                className="field-input text-xs py-1.5"
                placeholder="Ad Soyad"
                value={newStudentName}
                onChange={(e) => setNewStudentName(e.target.value)}
              />
              <input
                className="field-input text-xs py-1.5"
                placeholder="Öğrenci Numarası"
                value={newStudentNumber}
                onChange={(e) => setNewStudentNumber(e.target.value)}
              />
              <div className="flex flex-col gap-1">
                <label className="text-xs text-muted">Doğum Tarihi</label>
                <input
                  type="date"
                  className="field-input text-xs py-1.5"
                  value={newStudentBirthDate}
                  onChange={(e) => setNewStudentBirthDate(e.target.value)}
                />
              </div>
              <button
                className="btn btn-primary text-xs py-1.5"
                disabled={registerSaving}
                onClick={handleRegisterStudent}
              >
                {registerSaving ? "Kaydediliyor…" : "Kaydet"}
              </button>
              {registerError && <p className="text-primary text-xs">{registerError}</p>}
              {registerResult && (
                <div className="bg-green-50 border border-green-200 rounded-lg p-2 text-xs text-green-800">
                  <p className="font-semibold mb-0.5">Kayıt oluşturuldu — giriş bilgileri:</p>
                  <p>E-posta: <span className="font-mono">{registerResult.email}</span></p>
                  <p>Şifre: <span className="font-mono">{registerResult.password}</span></p>
                  <p className="text-[0.65rem] text-green-700 mt-1">Bu şifre yalnızca burada gösterilir, öğrenciye iletin.</p>
                </div>
              )}
            </div>

            <div className="flex items-center justify-between mb-3">
              <h2 className="section-heading mb-0">Öğrenciler ({students.length})</h2>
              {advanceSelection.size > 0 && (
                <button
                  className="btn btn-primary text-[0.65rem] py-1 px-2"
                  disabled={advancing}
                  onClick={handleAdvanceSemester}
                >
                  {advancing ? "İlerletiliyor…" : `${advanceSelection.size} öğrenciyi ilerlet →`}
                </button>
              )}
            </div>
            <p className="text-[0.65rem] text-muted mb-2">Dönem sonunda seçilen öğrencileri bir sonraki yarıyıla ilerletir.</p>
            <div className="flex flex-col gap-1">
              {students.map((s) => (
                <div
                  key={s.Id}
                  className={`flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all ${selectedStudentId === s.Id ? "bg-primary/10" : "hover:bg-gray-50"}`}
                >
                  <input
                    type="checkbox"
                    className="accent-primary shrink-0"
                    checked={advanceSelection.has(s.Id)}
                    onChange={() => toggleAdvanceSelection(s.Id)}
                  />
                  <button
                    onClick={() => { setSelectedStudentId(s.Id); handleLoadStudentGrades(s.Id); setGradeForm({ courseId: 0, grade: "AA", passed: true }); }}
                    className={`flex-1 min-w-0 text-left ${selectedStudentId === s.Id ? "text-primary font-semibold" : "text-ink"}`}
                  >
                    <div className="font-medium truncate">{s.Name}</div>
                    <div className="text-xs text-muted">{s.StudentNumber}</div>
                  </button>
                  <span className="text-[0.6rem] text-muted whitespace-nowrap bg-bg-page rounded-full px-2 py-0.5">
                    {s.CurrentSemester}. yarıyıl
                  </span>
                </div>
              ))}
            </div>
          </div>

          {/* Notlar paneli */}
          <div className="card p-5">
            {selectedStudentId === 0 ? (
              <p className="text-sm text-muted italic">Soldan bir öğrenci seçin.</p>
            ) : (() => {
              const student = students.find((s) => s.Id === selectedStudentId);
              const semLabels = ["","1. Yarıyıl","2. Yarıyıl","3. Yarıyıl","4. Yarıyıl","5. Yarıyıl","6. Yarıyıl","7. Yarıyıl","8. Yarıyıl"];
              const gradesBySem = studentGrades.reduce<Record<number, StudentGrade[]>>((acc, g) => {
                (acc[g.CourseSemester] ??= []).push(g);
                return acc;
              }, {});
              return (
                <>
                  <h2 className="section-heading mb-1">{student?.Name}</h2>
                  <p className="text-xs text-muted mb-4">{student?.StudentNumber}</p>

                  {/* Not girişi */}
                  <div className="bg-gray-50 rounded-xl p-4 mb-5 border border-line">
                    <h3 className="text-sm font-semibold mb-3">Not Ekle / Güncelle</h3>
                    <div className="flex gap-2 flex-wrap items-end">
                      <div className="flex flex-col gap-1">
                        <label className="text-xs text-muted">Ders</label>
                        <select className="field-input text-xs py-1.5 min-w-48"
                          value={gradeForm.courseId}
                          onChange={(e) => setGradeForm((f) => ({ ...f, courseId: Number(e.target.value) }))}>
                          <option value={0}>— Ders seçin —</option>
                          {courses.sort((a, b) => a.Semester - b.Semester || a.Title.localeCompare(b.Title)).map((c) => (
                            <option key={c.Id} value={c.Id}>{semLabels[c.Semester]} — {c.Title}</option>
                          ))}
                        </select>
                      </div>
                      <div className="flex flex-col gap-1">
                        <label className="text-xs text-muted">Harf Notu</label>
                        <select className="field-input text-xs py-1.5"
                          value={gradeForm.grade}
                          onChange={(e) => {
                            const g = e.target.value;
                            setGradeForm((f) => ({ ...f, grade: g, passed: g !== "FF" }));
                          }}>
                          {["AA","BA","BB","CB","CC","DC","DD","FF"].map((g) => <option key={g}>{g}</option>)}
                        </select>
                      </div>
                      <div className="flex flex-col gap-1">
                        <label className="text-xs text-muted">Durum</label>
                        <span className={`field-input text-xs py-1.5 flex items-center justify-center font-semibold ${gradeForm.grade !== "FF" ? "text-green-700" : "text-red-700"}`}>
                          {gradeForm.grade !== "FF" ? "Geçti" : "Kaldı"}
                        </span>
                      </div>
                      <button className="btn btn-primary text-xs py-1.5 px-4" onClick={handleUpsertGrade}>Kaydet</button>
                    </div>
                  </div>

                  {/* Transkript */}
                  {studentGrades.length === 0 ? (
                    <p className="text-sm text-muted italic">Henüz not girilmemiş.</p>
                  ) : (
                    Object.entries(gradesBySem).sort(([a],[b]) => Number(a)-Number(b)).map(([sem, grades]) => (
                      <div key={sem} className="mb-4">
                        <h4 className="text-xs font-semibold text-muted uppercase tracking-wide mb-2">{semLabels[Number(sem)]}</h4>
                        <div className="rounded-lg border border-line overflow-hidden">
                          <table className="w-full text-sm border-collapse">
                            <thead>
                              <tr className="bg-gray-50 text-xs text-muted">
                                <th className="text-left px-3 py-2">Ders</th>
                                <th className="text-center px-3 py-2 w-16">Not</th>
                                <th className="text-center px-3 py-2 w-20">Durum</th>
                                <th className="px-3 py-2 w-10"></th>
                              </tr>
                            </thead>
                            <tbody>
                              {grades.map((g) => (
                                <tr key={g.Id} className="border-t border-line">
                                  <td className="px-3 py-2 text-ink">{g.CourseTitle}</td>
                                  <td className="px-3 py-2 text-center font-bold">{g.Grade}</td>
                                  <td className="px-3 py-2 text-center">
                                    <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${g.Passed ? "bg-green-100 text-green-800" : "bg-red-100 text-red-800"}`}>
                                      {g.Passed ? "Geçti" : "Kaldı"}
                                    </span>
                                  </td>
                                  <td className="px-3 py-2 text-center">
                                    <button className="text-red-400 hover:text-red-600 text-xs" onClick={() => handleDeleteGrade(g.Id)}>✕</button>
                                  </td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      </div>
                    ))
                  )}
                </>
              );
            })()}
          </div>
        </div>
      )}

      {/* Dersler görünümü */}
      {view === "courses" && admin && (
        <div className="grid grid-cols-[1fr_360px] gap-5 items-start">

          {/* Ders listesi */}
          <div className="card">
            <div className="status-bar">{status}</div>
            <div className="p-5">
              <h2 className="section-heading mb-4">Ders Listesi ({courses.length})</h2>
              <div className="overflow-x-auto rounded-lg border border-line">
                <table className="w-full text-sm border-collapse">
                  <thead>
                    <tr className="bg-gray-50 text-xs text-muted uppercase tracking-wider">
                      <th className="px-3 py-2 text-left border-b border-line">Ders Adı</th>
                      <th className="px-3 py-2 text-left border-b border-line">Öğretmen</th>
                      <th className="px-3 py-2 text-center border-b border-line">Saat</th>
                      <th className="px-3 py-2 text-left border-b border-line">Yarıyıl</th>
                      <th className="px-3 py-2 text-center border-b border-line">Tür</th>
                      <th className="px-3 py-2 border-b border-line"></th>
                    </tr>
                  </thead>
                  <tbody>
                    {courses.map((c, i) => {
                      const teacher = teachers.find((t) => t.Id === c.TeacherId);
                      return (
                        <tr key={c.Id} className={i % 2 === 0 ? "bg-white" : "bg-gray-50/40"}>
                          <td className="px-3 py-2 font-medium text-ink border-b border-line">{c.Title}</td>
                          <td className="px-3 py-2 text-muted border-b border-line text-xs">{teacher?.Name ?? `#${c.TeacherId}`}</td>
                          <td className="px-3 py-2 text-center border-b border-line">{c.WeeklyHours}h</td>
                          <td className="px-3 py-2 text-muted border-b border-line text-xs">{c.Semester}. Yarıyıl</td>
                          <td className="px-3 py-2 text-center border-b border-line">
                            <span className={`text-[0.65rem] px-1.5 py-0.5 rounded font-medium ${c.IsElective ? "bg-primary/10 text-primary" : "bg-gray-100 text-muted"}`}>
                              {c.IsElective ? "Seçmeli" : "Zorunlu"}
                            </span>
                          </td>
                          <td className="px-3 py-2 border-b border-line text-right">
                            <button
                              onClick={() => handleDeleteCourse(c.Id, c.Title)}
                              className="text-xs text-red-500 hover:text-red-700 font-medium transition-colors"
                            >Sil</button>
                          </td>
                        </tr>
                      );
                    })}
                    {courses.length === 0 && (
                      <tr><td colSpan={6} className="px-3 py-6 text-center text-muted italic text-sm">Ders bulunamadı.</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          </div>

          {/* Ders ekleme formu */}
          <div className="card p-5">
            <h2 className="section-heading">Yeni Ders Ekle</h2>

            <label className="field-label">Ders Adı</label>
            <input className="field-input mb-3" placeholder="Örn: Algoritma Analizi" value={newTitle} onChange={(e) => setNewTitle(e.target.value)} />

            <label className="field-label">Öğretmen</label>
            <select className="field-input mb-3" value={newTeacherId} onChange={(e) => setNewTeacherId(Number(e.target.value))}>
              <option value={0}>Öğretmen seçin</option>
              {teachers.map((t) => <option key={t.Id} value={t.Id}>{t.Name}</option>)}
            </select>

            <label className="field-label">Haftalık Saat</label>
            <select className="field-input mb-3" value={newWeeklyHours} onChange={(e) => setNewWeeklyHours(Number(e.target.value))}>
              {[1,2,3,4,6].map((h) => <option key={h} value={h}>{h} saat</option>)}
            </select>

            <label className="field-label">Yarıyıl</label>
            <select className="field-input mb-3" value={newSemester} onChange={(e) => setNewSemester(Number(e.target.value))}>
              <option value={0}>Yarıyıl seçin</option>
              {[1,2,3,4,5,6,7,8].map((s) => <option key={s} value={s}>{s}. Yarıyıl</option>)}
            </select>

            <label className="field-label">Tahmini Öğrenci Sayısı</label>
            <input className="field-input mb-3" type="number" min={1} value={newExpectedCount} onChange={(e) => setNewExpectedCount(Number(e.target.value))} />

            <label className="flex items-center gap-2 mb-3 cursor-pointer">
              <input type="checkbox" className="accent-primary" checked={newIsElective} onChange={(e) => setNewIsElective(e.target.checked)} />
              <span className="text-sm text-ink">Seçmeli ders</span>
            </label>

            {newIsElective && (
              <>
                <label className="field-label">Seçmeli Grup Adı</label>
                <input className="field-input mb-3" placeholder="Örn: Teknik-9" value={newElectiveGroup} onChange={(e) => setNewElectiveGroup(e.target.value)} />
              </>
            )}

            {addError && <p className="text-primary text-xs mb-3">{addError}</p>}
            <button className="btn btn-primary w-full" onClick={handleAddCourse}>Ders Ekle</button>
          </div>
        </div>
      )}
    </div>
  );
}
