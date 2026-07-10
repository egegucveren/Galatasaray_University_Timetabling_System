namespace GsuTimetablingSystem.Models
{
    public class TeacherStudentCourse
    {
        public int     StudentId      { get; set; }
        public string  StudentName    { get; set; } = string.Empty;
        public string  StudentNumber  { get; set; } = string.Empty;
        public int     CourseId       { get; set; }
        public string  CourseTitle    { get; set; } = string.Empty;
        public int     CourseSemester { get; set; }
        public int?    GradeId        { get; set; }
        public string? Grade          { get; set; }
        public bool?   Passed         { get; set; }
    }

    public class PrerequisiteCheckResult
    {
        public int          CourseId             { get; set; }
        public string       CourseTitle          { get; set; } = string.Empty;
        public List<string> MissingPrerequisites { get; set; } = new();
    }
}
