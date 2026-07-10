using GsuTimetablingSystem.Models;
using GsuTimetablingSystem.Services;
using Xunit;

namespace GsuTimetablingSystem.Tests
{
    public class BacktrackingSchedulerServiceTests
    {
        private static TimeSlot Slot(int id, string day, string hourRange) =>
            new() { Id = id, Day = day, HourRange = hourRange };

        private static Teacher SimpleTeacher(int id, string name) =>
            new() { Id = id, Name = name, Email = $"{name.ToLowerInvariant()}@gsu.edu.tr", Password = string.Empty };

        private static Room SimpleRoom(int id, string name, int capacity, bool isAmphi = false) =>
            new() { Id = id, Name = name, Capacity = capacity, IsAmphi = isAmphi };

        private static Course SimpleCourse(int id, int teacherId, int weeklyHours, int semester, int expectedStudentCount = 20) =>
            new()
            {
                Id = id,
                Title = $"Ders {id}",
                TeacherId = teacherId,
                WeeklyHours = weeklyHours,
                IsElective = false,
                ElectiveGroup = string.Empty,
                ExpectedStudentCount = expectedStudentCount,
                Semester = semester
            };

        [Fact]
        public void SingleCourse_WithAvailableSlotAndRoom_SchedulesSuccessfully()
        {
            var teachers = new List<Teacher> { SimpleTeacher(1, "Ogretmen1") };
            var rooms = new List<Room> { SimpleRoom(1, "D201", 50) };
            var timeSlots = new List<TimeSlot>
            {
                Slot(1, "Pazartesi", "09:00-10:00"),
                Slot(2, "Pazartesi", "10:00-11:00"),
                Slot(3, "Pazartesi", "11:00-12:00"),
            };
            var courses = new List<Course> { SimpleCourse(id: 1, teacherId: 1, weeklyHours: 2, semester: 1) };

            var scheduler = new BacktrackingSchedulerService();
            var success = scheduler.TryGenerateSchedule(
                courses, teachers, rooms, timeSlots,
                manualAssignments: null,
                existingPlacements: new List<ScheduleResult>(),
                out var results,
                out var errorMessage);

            Assert.True(success, errorMessage);
            Assert.Single(results);
            Assert.Equal(2, results[0].DurationHours);
            Assert.Equal(1, results[0].CourseId);
        }

        [Fact]
        public void TwoCoursesSharingTeacherAndRoom_WithOnlyOneSlot_FailsGracefully()
        {
            // Aynı öğretmene ait iki ders, sadece TEK bir zaman dilimi ve TEK bir odayla
            // asla çakışmasız yerleştirilemez — algoritma bunu tespit edip false dönmeli,
            // istisna fırlatmamalı veya sessizce yanlış bir program üretmemeli.
            var teachers = new List<Teacher> { SimpleTeacher(1, "Ogretmen1") };
            var rooms = new List<Room> { SimpleRoom(1, "D201", 50) };
            var timeSlots = new List<TimeSlot> { Slot(1, "Pazartesi", "09:00-10:00") };
            var courses = new List<Course>
            {
                SimpleCourse(id: 1, teacherId: 1, weeklyHours: 1, semester: 1),
                SimpleCourse(id: 2, teacherId: 1, weeklyHours: 1, semester: 1),
            };

            var scheduler = new BacktrackingSchedulerService();
            var success = scheduler.TryGenerateSchedule(
                courses, teachers, rooms, timeSlots,
                manualAssignments: null,
                existingPlacements: new List<ScheduleResult>(),
                out var results,
                out var errorMessage);

            Assert.False(success);
            Assert.Empty(results);
            Assert.False(string.IsNullOrWhiteSpace(errorMessage));
        }

        [Fact]
        public void TwoIndependentCourses_NeverShareRoomOrTeacherAtSameSlot()
        {
            // İki farklı öğretmen/yarıyıla ait ders, yeterli oda ve zaman dilimiyle
            // çakışmasız yerleştirilebilmeli; sonuçta hiçbir (oda, saat) veya
            // (öğretmen, saat) ikilisi birden fazla kez kullanılmamalı.
            var teachers = new List<Teacher> { SimpleTeacher(1, "Ogretmen1"), SimpleTeacher(2, "Ogretmen2") };
            var rooms = new List<Room> { SimpleRoom(1, "D201", 50), SimpleRoom(2, "D202", 50) };
            var timeSlots = new List<TimeSlot>
            {
                Slot(1, "Pazartesi", "09:00-10:00"),
                Slot(2, "Pazartesi", "10:00-11:00"),
                Slot(3, "Sali", "09:00-10:00"),
                Slot(4, "Sali", "10:00-11:00"),
            };
            var courses = new List<Course>
            {
                SimpleCourse(id: 1, teacherId: 1, weeklyHours: 2, semester: 1),
                SimpleCourse(id: 2, teacherId: 2, weeklyHours: 2, semester: 3),
            };

            var scheduler = new BacktrackingSchedulerService();
            var success = scheduler.TryGenerateSchedule(
                courses, teachers, rooms, timeSlots,
                manualAssignments: null,
                existingPlacements: new List<ScheduleResult>(),
                out var results,
                out var errorMessage);

            Assert.True(success, errorMessage);
            Assert.Equal(2, results.Count);

            var roomSlotPairs = results.Select(r => (r.RoomId, r.TimeSlotId, r.Day)).ToList();
            Assert.Equal(roomSlotPairs.Count, roomSlotPairs.Distinct().Count());

            var teacherSlotPairs = results.Select(r => (r.TeacherId, r.TimeSlotId, r.Day)).ToList();
            Assert.Equal(teacherSlotPairs.Count, teacherSlotPairs.Distinct().Count());
        }
    }
}
