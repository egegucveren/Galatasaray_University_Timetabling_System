using System.Collections.Generic;
using System.Linq;
using GsuTimetablingSystem.Models;

namespace GsuTimetablingSystem.Services
{
    // Not: Bu sınıf daha önce "GoogleOrToolsService" olarak adlandırılmıştı, ancak gerçek
    // Google OR-Tools kütüphanesi hiç kullanılmıyordu (csproj'da böyle bir paket referansı
    // da yok). Burada tamamen elle yazılmış, geri izlemeli (backtracking) bir yerleştirme
    // algoritması var; isim, gerçekte ne yaptığını doğru yansıtması için değiştirildi.
    // Davranış/işlev bu değişiklikten etkilenmedi.
    public class BacktrackingSchedulerService
    {
        public bool TryGenerateSchedule(
            List<Course> courses,
            List<Teacher> teachers,
            List<Room> rooms,
            List<TimeSlot> timeSlots,
            IReadOnlyDictionary<int, List<ManualScheduleAssignment>>? manualAssignments,
            IReadOnlyList<ScheduleResult> existingPlacements,
            out List<ScheduleResult> results,
            out string errorMessage)
        {
            var orderedTimeSlots = timeSlots
                .OrderBy(slot => GetDaySortKey(slot.Day))
                .ThenBy(slot => GetStartMinutes(slot.HourRange))
                .ToList();
            var slotById = orderedTimeSlots.ToDictionary(slot => slot.Id);
            var scheduledResults = new List<ScheduleResult>();
            results = scheduledResults;
            errorMessage = string.Empty;
            var roomSlotUsage = new HashSet<(int RoomId, int SlotId)>();
            var teacherSlotUsage = new HashSet<(int TeacherId, int SlotId)>();
            var electiveGroupSlotUsage = new HashSet<(string Group, int SlotId)>();
            var semesterSlotUsage = new HashSet<(int Semester, int SlotId)>();
            var teacherById = teachers.ToDictionary(teacher => teacher.Id);
            var roomById = rooms.ToDictionary(room => room.Id);
            manualAssignments ??= new Dictionary<int, List<ManualScheduleAssignment>>();

            // Diğer yarıyıllardan gelen mevcut yerleştirmeleri çakışma setlerine ekle.
            // NOT: Sadece sınıf ve öğretmen çakışmaları eklenir.
            // Farklı yarıyıllar ayrı dönemlerde işlendiğinden aynı yarıyıl (öğrenci kohortu) çakışmaları eklenmez.
            foreach (var existing in existingPlacements)
            {
                var coveredSlots = GetConsecutiveSlots(orderedTimeSlots, slotById, existing.TimeSlotId, existing.DurationHours);
                if (coveredSlots is null) continue;
                foreach (var slot in coveredSlots)
                {
                    roomSlotUsage.Add((existing.RoomId, slot.Id));
                    teacherSlotUsage.Add((existing.TeacherId, slot.Id));
                }
            }
            // Zorunlu + büyük dersler önce gelsin (amfi gerektiren kurslar zor kısıt)
            var orderedCourses = courses
                .Where(course => course.WeeklyHours > 0) // Staj gibi sıfır saatli dersleri atla
                .OrderBy(course => course.IsElective ? 1 : 0)           // zorunlu önce
                .ThenByDescending(course => course.ExpectedStudentCount) // kalabalık önce
                .ThenBy(course => course.Id)
                .ToList();

            foreach (var course in orderedCourses)
            {
                if (!manualAssignments.TryGetValue(course.Id, out var courseAssignments) || courseAssignments.Count == 0)
                {
                    continue;
                }

                if (!teacherById.TryGetValue(course.TeacherId, out var teacher))
                {
                    errorMessage = $"Ders {course.Title} icin ogretmen bulunamadi.";
                    return false;
                }

                var totalManualDuration = courseAssignments.Sum(item => Math.Max(1, item.DurationHours));
                if (totalManualDuration != Math.Max(1, course.WeeklyHours))
                {
                    errorMessage = $"{course.Title} icin manuel oturumlarin toplam suresi haftalik ders saatiyle ayni olmali.";
                    return false;
                }

                foreach (var assignment in courseAssignments.OrderBy(item => item.StartTimeSlotId))
                {
                    if (!roomById.TryGetValue(assignment.RoomId, out var room))
                    {
                        errorMessage = $"Ders {course.Title} icin secilen oda bulunamadi.";
                        return false;
                    }

                    var manualDuration = assignment.DurationHours > 0 ? assignment.DurationHours : course.WeeklyHours;
                    var manualSlots = GetConsecutiveSlots(
                        orderedTimeSlots,
                        slotById,
                        assignment.StartTimeSlotId,
                        manualDuration);
                    if (manualSlots is null)
                    {
                        continue;
                    }

                    if (!TryPlaceCourse(
                            course,
                            teacher,
                            room,
                            manualSlots,
                            manualDuration,
                            true,
                            scheduledResults,
                            roomSlotUsage,
                            teacherSlotUsage,
                            electiveGroupSlotUsage,
                            semesterSlotUsage,
                            out errorMessage))
                    {
                        return false;
                    }
                }
            }

            // Randomize: slot ve oda sıralamasını karıştır
            var rng = Random.Shared;
            var shuffledSlots = orderedTimeSlots.OrderBy(_ => rng.Next()).ToList();
            var shuffledRooms  = rooms.OrderBy(_ => rng.Next()).ToList();

            // Her dersi max 2 saatlik oturumlara böl (örn. 3 saatlik ders → 2h + 1h)
            // Böylece bir ders birden fazla günde var olabilir.
            var sessions = new List<(Course Course, int Duration)>();
            foreach (var course in orderedCourses)
            {
                if (manualAssignments.ContainsKey(course.Id))
                {
                    sessions.Add((course, 0)); // 0 = manuel atanmış, geç
                    continue;
                }
                var rem = Math.Max(1, course.WeeklyHours);
                while (rem > 0)
                {
                    var d = Math.Min(2, rem);
                    sessions.Add((course, d));
                    rem -= d;
                }
            }

            // Her ders için hangi günlerde oturum yerleştirildiğini takip et
            var courseUsedDays = new Dictionary<int, HashSet<string>>(capacity: orderedCourses.Count);

            bool TryAssignSession(int idx)
            {
                if (idx == sessions.Count) return true;

                var (course, duration) = sessions[idx];

                // Manuel atanmış ders → atla
                if (duration == 0) return TryAssignSession(idx + 1);

                if (!teacherById.TryGetValue(course.TeacherId, out var teacher))
                    return false;

                courseUsedDays.TryGetValue(course.Id, out var usedDays);

                // Geçiş 1: Bu ders için henüz kullanılmayan günleri dene (farklı gün tercihi)
                foreach (var startSlot in shuffledSlots)
                {
                    if (usedDays != null && usedDays.Count > 0 && usedDays.Contains(startSlot.Day))
                        continue;

                    var slots = GetConsecutiveSlots(orderedTimeSlots, slotById, startSlot.Id, duration);
                    if (slots is null) continue;

                    foreach (var room in shuffledRooms)
                    {
                        if (!TryPlaceCourse(course, teacher, room, slots, duration, false,
                                scheduledResults, roomSlotUsage, teacherSlotUsage,
                                electiveGroupSlotUsage, semesterSlotUsage, out _))
                            continue;

                        if (!courseUsedDays.ContainsKey(course.Id))
                            courseUsedDays[course.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        courseUsedDays[course.Id].Add(startSlot.Day);

                        if (TryAssignSession(idx + 1)) return true;

                        RemovePlacement(course, teacher, room, slots, scheduledResults,
                            roomSlotUsage, teacherSlotUsage, electiveGroupSlotUsage, semesterSlotUsage);
                        courseUsedDays[course.Id].Remove(startSlot.Day);
                        if (courseUsedDays[course.Id].Count == 0) courseUsedDays.Remove(course.Id);
                    }
                }

                // Geçiş 2 (yedek): Farklı gün bulunamazsa aynı günü de kabul et
                if (usedDays != null && usedDays.Count > 0)
                {
                    foreach (var startSlot in shuffledSlots)
                    {
                        if (!usedDays.Contains(startSlot.Day)) continue;

                        var slots = GetConsecutiveSlots(orderedTimeSlots, slotById, startSlot.Id, duration);
                        if (slots is null) continue;

                        foreach (var room in shuffledRooms)
                        {
                            if (!TryPlaceCourse(course, teacher, room, slots, duration, false,
                                    scheduledResults, roomSlotUsage, teacherSlotUsage,
                                    electiveGroupSlotUsage, semesterSlotUsage, out _))
                                continue;

                            if (TryAssignSession(idx + 1)) return true;

                            RemovePlacement(course, teacher, room, slots, scheduledResults,
                                roomSlotUsage, teacherSlotUsage, electiveGroupSlotUsage, semesterSlotUsage);
                        }
                    }
                }

                return false;
            }

            if (TryAssignSession(0))
            {
                results = scheduledResults
                    .OrderBy(item => GetDaySortKey(item.Day))
                    .ThenBy(item => GetStartMinutes(item.HourRange))
                    .ThenBy(item => item.RoomId)
                    .ToList();
                return true;
            }

            errorMessage = "Manuel yerlestirmelerle birlikte cakismasiz program olusturulamadi.";
            results = new List<ScheduleResult>();
            return false;
        }


        private static bool TryPlaceCourse(
            Course course,
            Teacher teacher,
            Room room,
            IReadOnlyList<TimeSlot> slots,
            int durationHours,
            bool isManual,
            List<ScheduleResult> results,
            HashSet<(int RoomId, int SlotId)> roomSlotUsage,
            HashSet<(int TeacherId, int SlotId)> teacherSlotUsage,
            HashSet<(string Group, int SlotId)> electiveGroupSlotUsage,
            HashSet<(int Semester, int SlotId)> semesterSlotUsage,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            if (slots.Count == 0)
            {
                errorMessage = $"{course.Title} icin gecerli zaman dilimi bulunamadi.";
                return false;
            }

            foreach (var slot in slots)
            {
                if (teacher.UnavailabilitySlots.Contains(slot.Id))
                {
                    errorMessage = $"{teacher.Name} {slot.Day} {slot.HourRange} saatinde musait degil.";
                    return false;
                }

                if (teacherSlotUsage.Contains((teacher.Id, slot.Id)))
                {
                    errorMessage = $"{teacher.Name} ayni zaman diliminde iki derse atanamaz.";
                    return false;
                }

                if (roomSlotUsage.Contains((room.Id, slot.Id)))
                {
                    errorMessage = $"{room.Name} ayni zaman diliminde dolu.";
                    return false;
                }
            }

            if (course.ExpectedStudentCount > room.Capacity)
            {
                errorMessage = $"{room.Name} dersi kapasite olarak karsilamiyor.";
                return false;
            }

            if (course.ExpectedStudentCount > 60 && !room.IsAmphi)
            {
                errorMessage = $"{course.Title} icin amfi gerekli.";
                return false;
            }

            foreach (var slot in slots)
            {
                if (semesterSlotUsage.Contains((course.Semester, slot.Id)))
                {
                    errorMessage = $"{course.Semester}. yarıyıl ogrencileri bu zaman diliminde baska bir derste.";
                    return false;
                }

                var electiveKey = (course.ElectiveGroup, slot.Id);
                if (course.IsElective &&
                    !string.IsNullOrWhiteSpace(course.ElectiveGroup) &&
                    electiveGroupSlotUsage.Contains(electiveKey))
                {
                    errorMessage = $"{course.ElectiveGroup} secmeli grubu ayni saatte cakisiyor.";
                    return false;
                }
            }

            foreach (var slot in slots)
            {
                roomSlotUsage.Add((room.Id, slot.Id));
                teacherSlotUsage.Add((teacher.Id, slot.Id));
                semesterSlotUsage.Add((course.Semester, slot.Id));

                if (course.IsElective && !string.IsNullOrWhiteSpace(course.ElectiveGroup))
                {
                    electiveGroupSlotUsage.Add((course.ElectiveGroup, slot.Id));
                }
            }

            results.Add(new ScheduleResult
            {
                CourseId = course.Id,
                CourseTitle = course.Title,
                TeacherId = teacher.Id,
                IsElective = course.IsElective,
                ElectiveGroup = course.ElectiveGroup,
                Semester = course.Semester,
                TeacherName = teacher.Name,
                RoomId = room.Id,
                RoomName = room.Name,
                IsAmphi = room.IsAmphi,
                TimeSlotId = slots[0].Id,
                DurationHours = durationHours,
                Day = slots[0].Day,
                HourRange = MergeHourRange(slots),
                IsManual = isManual
            });

            return true;
        }

        private static void RemovePlacement(
            Course course,
            Teacher teacher,
            Room room,
            IReadOnlyList<TimeSlot> slots,
            List<ScheduleResult> results,
            HashSet<(int RoomId, int SlotId)> roomSlotUsage,
            HashSet<(int TeacherId, int SlotId)> teacherSlotUsage,
            HashSet<(string Group, int SlotId)> electiveGroupSlotUsage,
            HashSet<(int Semester, int SlotId)> semesterSlotUsage)
        {
            var existing = results.FirstOrDefault(item => item.CourseId == course.Id && item.RoomId == room.Id && item.TimeSlotId == slots[0].Id);
            if (existing is not null)
            {
                results.Remove(existing);
            }

            foreach (var slot in slots)
            {
                roomSlotUsage.Remove((room.Id, slot.Id));
                teacherSlotUsage.Remove((teacher.Id, slot.Id));
                semesterSlotUsage.Remove((course.Semester, slot.Id));

                if (course.IsElective && !string.IsNullOrWhiteSpace(course.ElectiveGroup))
                {
                    electiveGroupSlotUsage.Remove((course.ElectiveGroup, slot.Id));
                }
            }
        }

        private static List<TimeSlot>? GetConsecutiveSlots(
            IReadOnlyList<TimeSlot> orderedTimeSlots,
            IReadOnlyDictionary<int, TimeSlot> slotById,
            int startTimeSlotId,
            int durationHours)
        {
            if (durationHours <= 0 || !slotById.TryGetValue(startTimeSlotId, out var startSlot))
            {
                return null;
            }

            var startIndex = orderedTimeSlots
                .Select((slot, index) => new { slot, index })
                .FirstOrDefault(item => item.slot.Id == startSlot.Id)?.index;
            if (startIndex is null)
            {
                return null;
            }

            var result = new List<TimeSlot>();
            for (var offset = 0; offset < durationHours; offset++)
            {
                var index = startIndex.Value + offset;
                if (index >= orderedTimeSlots.Count)
                {
                    return null;
                }

                var current = orderedTimeSlots[index];
                if (!current.Day.Equals(startSlot.Day, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (result.Count > 0)
                {
                    var previousEnd = GetEndMinutes(result[^1].HourRange);
                    var currentStart = GetStartMinutes(current.HourRange);
                    if (currentStart != previousEnd)
                    {
                        return null;
                    }
                }

                result.Add(current);
            }

            return result;
        }

        private static string MergeHourRange(IReadOnlyList<TimeSlot> slots)
        {
            var firstStart = slots[0].HourRange.Split('-', 2)[0].Trim();
            var lastEnd = slots[^1].HourRange.Split('-', 2)[1].Trim();
            return $"{firstStart}-{lastEnd}";
        }

        private static int GetDaySortKey(string day) => day switch
        {
            "Pazartesi" => 1,
            "Sali" => 2,
            "Carsamba" => 3,
            "Persembe" => 4,
            "Cuma" => 5,
            _ => 99
        };

        private static int GetStartMinutes(string range)
        {
            var start = range.Split('-', 2)[0].Trim();
            return GetMinutes(start);
        }

        private static int GetEndMinutes(string range)
        {
            var end = range.Split('-', 2)[1].Trim();
            return GetMinutes(end);
        }

        private static int GetMinutes(string clock)
        {
            var parts = clock.Split(':', 2);
            return (int.Parse(parts[0]) * 60) + int.Parse(parts[1]);
        }
    }
}
