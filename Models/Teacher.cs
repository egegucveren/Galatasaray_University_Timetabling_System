namespace GsuTimetablingSystem.Models
{
    public class Teacher
    {
        public int Id { get; set; }

        // Girişte kullanılan, internal id'den ayrı numara (öğrencinin student_number'ına paralel).
        public string TeacherNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        // Müsait olmadığı timeslot ID'leri
        public List<int> UnavailabilitySlots { get; set; } = new();

        // Girişte üretilir; sonraki isteklerde X-Teacher-Token header'ında gönderilir.
        public string? Token { get; set; }
    }
}
