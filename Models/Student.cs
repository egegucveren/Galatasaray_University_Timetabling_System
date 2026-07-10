namespace GsuTimetablingSystem.Models
{
    public class Student
    {
        public int Id { get; set; }
        public string StudentNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public DateOnly? BirthDate { get; set; }

        // Öğrencinin şu an kayıt talebi oluşturabileceği yarıyıl. Yeni öğrenci 1 ile başlar;
        // admin, dönem sonunda öğrenciyi "sonraki yarıyıla ilerlet" eylemiyle artırır.
        public int CurrentSemester { get; set; } = 1;

        // Girişte üretilir; sonraki isteklerde X-Student-Token header'ında gönderilir.
        public string? Token { get; set; }
    }
}
