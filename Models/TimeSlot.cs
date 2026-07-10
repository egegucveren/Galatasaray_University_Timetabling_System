namespace GsuTimetablingSystem.Models
{
    public class TimeSlot
    {
        public int Id { get; set; }
        public string Day { get; set; } = string.Empty; 
        public string HourRange { get; set; } = string.Empty; 
    }
}