namespace GsuTimetablingSystem.Models
{
    public class ManualScheduleAssignment
    {
        public int CourseId { get; set; }
        public int RoomId { get; set; }
        public int StartTimeSlotId { get; set; }
        public int DurationHours { get; set; }
    }
}
