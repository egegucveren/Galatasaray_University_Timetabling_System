namespace GsuTimetablingSystem.Models
{
    public class Enrollment
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string StudentNumber { get; set; } = string.Empty;
        public int Semester { get; set; }
        public string Status { get; set; } = "pending"; // pending | approved | rejected
        public DateTime CreatedAt { get; set; }
        public string ReviewerNote { get; set; } = string.Empty;
        public List<int> ElectiveCourseIds { get; set; } = new();
    }
}
