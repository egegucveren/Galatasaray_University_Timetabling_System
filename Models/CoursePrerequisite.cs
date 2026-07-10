namespace GsuTimetablingSystem.Models
{
    // Bir dersin ön koşul tanımı (yönetim ekranı için) — CheckPrerequisitesAsync
    // sonucundan (PrerequisiteCheckResult) farklıdır; bu, ders bazında tanımlı
    // ön koşul KAYDINI temsil eder (ekle/sil işlemleri için).
    public class CoursePrerequisiteRecord
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        public string CourseTitle { get; set; } = string.Empty;
        public int PrerequisiteCourseId { get; set; }
        public string PrerequisiteCourseTitle { get; set; } = string.Empty;
        public int PrereqGroup { get; set; } = 1;
    }
}
