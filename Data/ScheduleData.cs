using GsuTimetablingSystem.Models;

namespace GsuTimetablingSystem.Data
{
    public class ScheduleData
    {
        public List<Course> Courses { get; set; } = new();
        public List<Teacher> Teachers { get; set; } = new();
        public List<Room> Rooms { get; set; } = new();
        public List<TimeSlot> TimeSlots { get; set; } = new();
    }
}
