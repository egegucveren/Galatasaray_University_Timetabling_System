import type {
  Admin,
  Course,
  CoursePrerequisite,
  Enrollment,
  ElectiveOption,
  PrerequisiteCheckResult,
  Room,
  ScheduleResult,
  Student,
  StudentGrade,
  Teacher,
  TeacherStudentCourse,
  TimeSlot,
} from "./types";

async function request<T>(url: string, options: RequestInit = {}): Promise<T> {
  const res = await fetch(url, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(options.headers ?? {}),
    },
  });

  const text = await res.text();
  let data: unknown = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    throw new Error(text?.slice(0, 300) || "Sunucu yanıtı okunamadı.");
  }

  if (!res.ok) {
    const msg =
      data !== null && typeof data === "object" && "Message" in data
        ? String((data as Record<string, unknown>).Message)
        : "İşlem başarısız.";
    throw new Error(msg);
  }

  return data as T;
}

function adminHeaders(token: string) {
  return { "X-Admin-Token": token };
}

function teacherHeaders(token: string) {
  return { "X-Teacher-Token": token };
}

function studentHeaders(token: string) {
  return { "X-Student-Token": token };
}

// ── Auth ─────────────────────────────────────────────────────────────────────

export const loginStudent = (studentNumber: string, password: string) =>
  request<Student>("/api/login/student", {
    method: "POST",
    body: JSON.stringify({ studentNumber, password }),
  });

export const loginTeacher = (teacherNumber: string, password: string) =>
  request<Teacher>("/api/login/teacher", {
    method: "POST",
    body: JSON.stringify({ teacherNumber, password }),
  });

export const loginAdmin = (email: string, password: string) =>
  request<Admin>("/api/login/admin", {
    method: "POST",
    body: JSON.stringify({ email, password }),
  });

export const logoutAdmin = (token: string) =>
  request<void>("/api/logout/admin", {
    method: "POST",
    headers: adminHeaders(token),
  });

export const logoutTeacher = (token: string) =>
  request<void>("/api/logout/teacher", {
    method: "POST",
    headers: teacherHeaders(token),
  });

export const logoutStudent = (token: string) =>
  request<void>("/api/logout/student", {
    method: "POST",
    headers: studentHeaders(token),
  });

// ── Data ─────────────────────────────────────────────────────────────────────

export const getTimeSlots = () => request<TimeSlot[]>("/api/time-slots");

export const getRooms = (token: string) =>
  request<Room[]>("/api/rooms", { headers: adminHeaders(token) });

export const getCourses = (token: string) =>
  request<Course[]>("/api/courses", { headers: adminHeaders(token) });

export const getTeachers = (token: string) =>
  request<Teacher[]>("/api/teachers", { headers: adminHeaders(token) });

export const addCourse = (
  token: string,
  body: {
    title: string;
    teacherId: number;
    weeklyHours: number;
    isElective: boolean;
    electiveGroup: string;
    expectedStudentCount: number;
    semester: number;
  }
) =>
  request<Course>("/api/courses", {
    method: "POST",
    headers: adminHeaders(token),
    body: JSON.stringify(body),
  });

export const deleteCourse = (token: string, id: number) =>
  request<void>(`/api/courses/${id}`, {
    method: "DELETE",
    headers: adminHeaders(token),
  });

// ── Enrollment ────────────────────────────────────────────────────────────────

export const getCoursesForSemester = (semester: number) =>
  request<Course[]>(`/api/courses/semester/${semester}`);

export const getStudentEnrollment = (studentId: number, token: string) =>
  request<Enrollment>(`/api/student/enrollment?studentId=${studentId}`, { headers: studentHeaders(token) });

export const createEnrollment = (studentId: number, semester: number, electiveCourseIds: number[], token: string) =>
  request<Enrollment>("/api/student/enrollment", {
    method: "POST",
    headers: studentHeaders(token),
    body: JSON.stringify({ studentId, semester, electiveCourseIds }),
  });

export const getAllEnrollments = (token: string) =>
  request<Enrollment[]>("/api/enrollments", { headers: adminHeaders(token) });

export const reviewEnrollment = (token: string, id: number, status: string, reviewerNote: string) =>
  request<Enrollment>(`/api/enrollments/${id}`, {
    method: "PUT",
    headers: adminHeaders(token),
    body: JSON.stringify({ status, reviewerNote }),
  });

// ── Student ───────────────────────────────────────────────────────────────────

export const getStudentElectives = (studentId: number, token: string) =>
  request<ElectiveOption[]>(`/api/student/electives?studentId=${studentId}`, { headers: studentHeaders(token) });

export const saveStudentElectives = (studentId: number, courseIds: number[], token: string) =>
  request<void>("/api/student/electives", {
    method: "POST",
    headers: studentHeaders(token),
    body: JSON.stringify({ studentId, courseIds }),
  });

export const getStudentSchedule = (studentId: number, token: string) =>
  request<ScheduleResult[]>(`/api/student/schedule?studentId=${studentId}`, { headers: studentHeaders(token) });

// Öğrencinin kendi transkriptini görüntülemesi için (admin tarafındaki
// getStudentGrades'ten farklı — admin token değil, öğrenci token'ı kullanır).
export const getMyStudentGrades = (studentId: number, token: string) =>
  request<StudentGrade[]>(`/api/student/grades?studentId=${studentId}`, { headers: studentHeaders(token) });

// ── Teacher ───────────────────────────────────────────────────────────────────

export const getTeacherAvailability = (teacherId: number, token: string) =>
  request<Teacher>(`/api/teacher/availability?teacherId=${teacherId}`, { headers: teacherHeaders(token) });

export const saveTeacherAvailability = (
  teacherId: number,
  unavailableTimeSlotIds: number[],
  token: string
) =>
  request<void>("/api/teacher/availability", {
    method: "POST",
    headers: teacherHeaders(token),
    body: JSON.stringify({ teacherId, unavailableTimeSlotIds }),
  });

export const getTeacherSchedule = (teacherId: number, token: string) =>
  request<ScheduleResult[]>(`/api/teacher/schedule?teacherId=${teacherId}`, { headers: teacherHeaders(token) });

// ── Admin schedule ────────────────────────────────────────────────────────────

export const getAdminSchedule = (token: string) =>
  request<ScheduleResult[]>("/api/schedule/admin", {
    headers: adminHeaders(token),
  });

export const generateSchedule = (token: string, semester = 0) =>
  request<ScheduleResult[]>("/api/schedule/generate", {
    method: "POST",
    headers: adminHeaders(token),
    body: JSON.stringify({ semester }),
  });

export const adjustSchedule = (
  token: string,
  body: {
    courseId: number;
    sessions?: { roomId: number; startTimeSlotId: number; durationHours: number }[];
    clearManualAssignment?: boolean;
  }
) =>
  request<ScheduleResult[]>("/api/schedule/adjust", {
    method: "POST",
    headers: adminHeaders(token),
    body: JSON.stringify(body),
  });

export const resetSchedule = (token: string) =>
  request<void>("/api/schedule/reset", {
    method: "POST",
    headers: adminHeaders(token),
  });

// ── Student Grades (Admin) ────────────────────────────────────────────────────

export const getAdminStudents = (token: string) =>
  request<Student[]>("/api/admin/students", { headers: adminHeaders(token) });

export const registerStudent = (
  token: string,
  body: { name: string; studentNumber: string; birthDate: string }
) =>
  request<{ Student: Student; GeneratedPassword: string }>("/api/admin/students/register", {
    method: "POST",
    headers: adminHeaders(token),
    body: JSON.stringify(body),
  });

export const advanceStudentsSemester = (token: string, studentIds: number[]) =>
  request<Student[]>("/api/admin/students/advance-semester", {
    method: "POST",
    headers: adminHeaders(token),
    body: JSON.stringify({ studentIds }),
  });

export const getStudentGrades = (token: string, studentId: number) =>
  request<StudentGrade[]>(`/api/admin/students/${studentId}/grades`, { headers: adminHeaders(token) });

export const upsertGrade = (token: string, studentId: number, courseId: number, grade: string, passed: boolean) =>
  request<StudentGrade>("/api/admin/grades", {
    method: "POST",
    headers: adminHeaders(token),
    body: JSON.stringify({ studentId, courseId, grade, passed }),
  });

export const deleteGrade = (token: string, gradeId: number) =>
  request<void>(`/api/admin/grades/${gradeId}`, {
    method: "DELETE",
    headers: adminHeaders(token),
  });

// ── Teacher Grades ────────────────────────────────────────────────────────────

export const getTeacherStudents = (teacherId: number, token: string) =>
  request<TeacherStudentCourse[]>(`/api/teacher/students?teacherId=${teacherId}`, { headers: teacherHeaders(token) });

export const upsertTeacherGrade = (studentId: number, courseId: number, grade: string, passed: boolean, token: string) =>
  request<StudentGrade>("/api/teacher/grades", {
    method: "POST",
    headers: teacherHeaders(token),
    body: JSON.stringify({ studentId, courseId, grade, passed }),
  });

// Sadece admin kayıt inceleme ekranından çağrılır — admin token gerekir.
export const checkPrerequisites = (studentId: number, semester: number, token: string) =>
  request<PrerequisiteCheckResult[]>(`/api/teacher/prerequisites?studentId=${studentId}&semester=${semester}`, {
    headers: adminHeaders(token),
  });

// ── Prerequisite management (teacher) ────────────────────────────────────────

export const getTeacherCourses = (teacherId: number, token: string) =>
  request<Course[]>(`/api/teacher/courses?teacherId=${teacherId}`, { headers: teacherHeaders(token) });

export const getCourseCatalog = () =>
  request<Course[]>("/api/courses/catalog");

export const getTeacherPrerequisites = (teacherId: number, token: string) =>
  request<CoursePrerequisite[]>(`/api/teacher/prerequisites/manage?teacherId=${teacherId}`, { headers: teacherHeaders(token) });

export const addPrerequisite = (
  teacherId: number,
  courseId: number,
  prerequisiteCourseId: number,
  prereqGroup: number,
  token: string
) =>
  request<CoursePrerequisite>("/api/teacher/prerequisites", {
    method: "POST",
    headers: teacherHeaders(token),
    body: JSON.stringify({ teacherId, courseId, prerequisiteCourseId, prereqGroup }),
  });

export const deletePrerequisite = (teacherId: number, id: number, token: string) =>
  request<void>(`/api/teacher/prerequisites/${id}?teacherId=${teacherId}`, {
    method: "DELETE",
    headers: teacherHeaders(token),
  });
