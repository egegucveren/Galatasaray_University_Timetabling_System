export interface Student {
  Id: number;
  Name: string;
  Email: string;
  StudentNumber: string;
  BirthDate?: string | null;
  CurrentSemester: number;
  Token: string;
}

export interface Teacher {
  Id: number;
  TeacherNumber: string;
  Name: string;
  Email: string;
  UnavailabilitySlots: number[];
  Token: string;
}

export interface Admin {
  Name: string;
  Email: string;
  Token: string;
}

export interface TimeSlot {
  Id: number;
  Day: string;
  HourRange: string;
}

export interface Room {
  Id: number;
  Name: string;
  Capacity: number;
  IsAmphi: boolean;
}

export interface Course {
  Id: number;
  Title: string;
  TeacherId: number;
  WeeklyHours: number;
  IsElective: boolean;
  ElectiveGroup: string;
  Semester: number;
}

export interface CoursePrerequisite {
  Id: number;
  CourseId: number;
  CourseTitle: string;
  PrerequisiteCourseId: number;
  PrerequisiteCourseTitle: string;
  PrereqGroup: number;
}

export interface Enrollment {
  Id: number;
  StudentId: number;
  StudentName: string;
  StudentNumber: string;
  Semester: number;
  Status: "pending" | "approved" | "rejected";
  CreatedAt: string;
  ReviewerNote: string;
}

export interface ScheduleResult {
  CourseId: number;
  CourseTitle: string;
  TeacherId: number;
  TeacherName: string;
  IsElective: boolean;
  ElectiveGroup: string;
  Semester: number;
  RoomId: number;
  RoomName: string;
  IsAmphi: boolean;
  TimeSlotId: number;
  DurationHours: number;
  Day: string;
  HourRange: string;
  IsManual: boolean;
}

export interface ElectiveOption {
  CourseId: number;
  Title: string;
  ElectiveGroup: string;
  TeacherName: string;
  IsSelected: boolean;
}

export interface StudentGrade {
  Id: number;
  StudentId: number;
  StudentName: string;
  CourseId: number;
  CourseTitle: string;
  CourseSemester: number;
  Grade: string;
  Passed: boolean;
  CreatedAt: string;
}

export interface TeacherStudentCourse {
  StudentId: number;
  StudentName: string;
  StudentNumber: string;
  CourseId: number;
  CourseTitle: string;
  CourseSemester: number;
  GradeId: number | null;
  Grade: string | null;
  Passed: boolean | null;
}

export interface PrerequisiteCheckResult {
  CourseId: number;
  CourseTitle: string;
  MissingPrerequisites: string[];
}

export interface SessionDraft {
  roomId: number;
  durationHours: number;
  day: string;
  startTime: string;
  isManualSource: boolean;
}
