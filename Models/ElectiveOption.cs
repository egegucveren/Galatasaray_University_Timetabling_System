namespace GsuTimetablingSystem.Models
{
    public class ElectiveOption
    {
        public int CourseId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ElectiveGroup { get; set; } = string.Empty;
        public string TeacherName { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }
}
