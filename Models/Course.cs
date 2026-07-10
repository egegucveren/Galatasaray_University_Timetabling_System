namespace GsuTimetablingSystem.Models
{
    public class Course
    {
        public int Id { get; set; }
        public string Title  { get; set; } = string.Empty;
        public int TeacherId { get; set; }
        public int WeeklyHours { get; set; }
        public bool IsElective { get; set; }
        public string ElectiveGroup { get; set; } = string.Empty;
        public int ExpectedStudentCount { get; set; }
        public int Semester { get; set; }
    }
}
