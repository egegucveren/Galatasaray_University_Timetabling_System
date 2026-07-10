namespace GsuTimetablingSystem.Models
{
    public class StudentGrade
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public int CourseId { get; set; }
        public string CourseTitle { get; set; } = string.Empty;
        public int CourseSemester { get; set; }
        public string Grade { get; set; } = string.Empty; // AA BA BB CB CC DC DD FF
        public bool Passed { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
