namespace GsuTimetablingSystem.Models
{
    public class StudentLoginRequest
    {
        public string StudentNumber { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class TeacherLoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class AdminLoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class StudentElectiveRequest
    {
        public int StudentId { get; set; }
        public List<int> CourseIds { get; set; } = new();
    }

    public class TeacherAvailabilityRequest
    {
        public int TeacherId { get; set; }
        public List<int> UnavailableTimeSlotIds { get; set; } = new();
    }

    public class ManualScheduleAdjustmentRequest
    {
        public int CourseId { get; set; }
        public List<ManualScheduleSessionRequest> Sessions { get; set; } = new();
        public bool ClearManualAssignment { get; set; }
    }

    public class ManualScheduleSessionRequest
    {
        public int? RoomId { get; set; }
        public int? StartTimeSlotId { get; set; }
        public int? DurationHours { get; set; }
    }

    public class AddCourseRequest
    {
        public string Title { get; set; } = string.Empty;
        public int TeacherId { get; set; }
        public int WeeklyHours { get; set; } = 2;
        public bool IsElective { get; set; }
        public string ElectiveGroup { get; set; } = string.Empty;
        public int ExpectedStudentCount { get; set; } = 30;
        public int Semester { get; set; }
    }

    public class CreateEnrollmentRequest
    {
        public int StudentId { get; set; }
        public int Semester { get; set; }
        public List<int> ElectiveCourseIds { get; set; } = new();
    }

    public class ReviewEnrollmentRequest
    {
        public string Status { get; set; } = string.Empty; // approved | rejected
        public string ReviewerNote { get; set; } = string.Empty;
    }

    public class GenerateScheduleRequest
    {
        public int Semester { get; set; } // 0 = tümü, 1-8 = belirli yarıyıl
    }

    public class UpsertGradeRequest
    {
        public int StudentId { get; set; }
        public int CourseId { get; set; }
        public string Grade { get; set; } = string.Empty; // AA BA BB CB CC DC DD FF
        public bool Passed { get; set; }
    }

    public class AddPrerequisiteRequest
    {
        public int TeacherId { get; set; }
        public int CourseId { get; set; }
        public int PrerequisiteCourseId { get; set; }
        public int PrereqGroup { get; set; } = 1;
    }

    public class RegisterStudentRequest
    {
        public string Name { get; set; } = string.Empty;
        public string StudentNumber { get; set; } = string.Empty;
        public string BirthDate { get; set; } = string.Empty; // "yyyy-MM-dd"
    }

    public class AdvanceSemesterRequest
    {
        public List<int> StudentIds { get; set; } = new();
    }
}
