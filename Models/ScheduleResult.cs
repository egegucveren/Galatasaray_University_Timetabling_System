namespace GsuTimetablingSystem.Models
{
    public class ScheduleResult
    {
        public int CourseId { get; set; }
        public string CourseTitle { get; set; } = string.Empty;
        public int TeacherId { get; set; }
        public bool IsElective { get; set; }
        public string ElectiveGroup { get; set; } = string.Empty;
        public int Semester { get; set; }
        public string TeacherName { get; set; } = string.Empty;
        public int RoomId { get; set; }
        public string RoomName { get; set; } = string.Empty;
        public bool IsAmphi { get; set; }
        public int TimeSlotId { get; set; }
        public int DurationHours { get; set; }
        public string Day { get; set; } = string.Empty;
        public string HourRange { get; set; } = string.Empty;
        public bool IsManual { get; set; }
    }
}
