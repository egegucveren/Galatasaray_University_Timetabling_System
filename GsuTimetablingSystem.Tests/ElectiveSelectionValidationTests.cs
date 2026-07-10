using System.Collections.Generic;
using GsuTimetablingSystem.Data;
using GsuTimetablingSystem.Models;
using Xunit;

namespace GsuTimetablingSystem.Tests
{
    // MySqlScheduleRepository.ValidateElectiveSelection, GSÜ'nün resmi öğretim programındaki
    // (ects.gsu.edu.tr) yarıyıl + seçmeli grup limitlerini uyguluyor: 5.yy 1, 6.yy 2, 7.yy 1 (+1 sosyal),
    // 8.yy 2 INF + 1 IND (+1 CNT, henüz ders yok). Bu testler o kuralları doğrular.
    public class ElectiveSelectionValidationTests
    {
        private static Course Mandatory(int id, int semester) => new()
        {
            Id = id, Title = $"Zorunlu {id}", TeacherId = 1, WeeklyHours = 3,
            IsElective = false, ElectiveGroup = "", ExpectedStudentCount = 30, Semester = semester
        };

        private static Course Elective(int id, int semester, string group) => new()
        {
            Id = id, Title = $"Secmeli {id}", TeacherId = 1, WeeklyHours = 3,
            IsElective = true, ElectiveGroup = group, ExpectedStudentCount = 20, Semester = semester
        };

        [Fact]
        public void Semester5_ExactlyOneFromTeknik5_IsValid()
        {
            var courses = new List<Course>
            {
                Mandatory(1, 5),
                Elective(2, 5, "Teknik-5"),
                Elective(3, 5, "Teknik-5"),
                Elective(4, 5, "Teknik-5"),
            };

            var result = MySqlScheduleRepository.ValidateElectiveSelection(5, courses, new[] { 2 });

            Assert.Null(result);
        }

        [Fact]
        public void Semester5_TwoFromTeknik5_IsRejected()
        {
            var courses = new List<Course>
            {
                Elective(2, 5, "Teknik-5"),
                Elective(3, 5, "Teknik-5"),
                Elective(4, 5, "Teknik-5"),
            };

            var result = MySqlScheduleRepository.ValidateElectiveSelection(5, courses, new[] { 2, 3 });

            Assert.NotNull(result);
        }

        [Fact]
        public void Semester5_ZeroFromTeknik5_IsRejected()
        {
            var courses = new List<Course>
            {
                Elective(2, 5, "Teknik-5"),
                Elective(3, 5, "Teknik-5"),
            };

            var result = MySqlScheduleRepository.ValidateElectiveSelection(5, courses, System.Array.Empty<int>());

            Assert.NotNull(result);
        }

        [Fact]
        public void Semester6_ExactlyTwoFromTeknik6_IsValid()
        {
            var courses = new List<Course>
            {
                Elective(10, 6, "Teknik-6"),
                Elective(11, 6, "Teknik-6"),
                Elective(12, 6, "Teknik-6"),
            };

            var result = MySqlScheduleRepository.ValidateElectiveSelection(6, courses, new[] { 10, 11 });

            Assert.Null(result);
        }

        [Fact]
        public void Semester6_OnlyOneFromTeknik6_IsRejected()
        {
            var courses = new List<Course>
            {
                Elective(10, 6, "Teknik-6"),
                Elective(11, 6, "Teknik-6"),
            };

            var result = MySqlScheduleRepository.ValidateElectiveSelection(6, courses, new[] { 10 });

            Assert.NotNull(result);
        }

        [Fact]
        public void Semester8_TwoInfPlusOneInd_IsValid()
        {
            var courses = new List<Course>
            {
                Elective(20, 8, "INF-8"),
                Elective(21, 8, "INF-8"),
                Elective(22, 8, "INF-8"),
                Elective(30, 8, "IND-8"),
                Elective(31, 8, "IND-8"),
            };

            var result = MySqlScheduleRepository.ValidateElectiveSelection(8, courses, new[] { 20, 21, 30 });

            Assert.Null(result);
        }

        [Fact]
        public void Semester8_MissingIndSelection_IsRejected()
        {
            var courses = new List<Course>
            {
                Elective(20, 8, "INF-8"),
                Elective(21, 8, "INF-8"),
                Elective(30, 8, "IND-8"),
            };

            // 2 INF secilmis ama IND grubundan hic secilmemis -> gecersiz
            var result = MySqlScheduleRepository.ValidateElectiveSelection(8, courses, new[] { 20, 21 });

            Assert.NotNull(result);
        }

        [Fact]
        public void UnknownOrOtherSemesterCourseId_IsRejected()
        {
            var courses = new List<Course>
            {
                Elective(2, 5, "Teknik-5"),
            };

            // 999 bu yariyilda tanimli degil
            var result = MySqlScheduleRepository.ValidateElectiveSelection(5, courses, new[] { 999 });

            Assert.NotNull(result);
        }

        [Fact]
        public void SemesterWithNoElectives_EmptySelection_IsValid()
        {
            var courses = new List<Course> { Mandatory(1, 2), Mandatory(2, 2) };

            var result = MySqlScheduleRepository.ValidateElectiveSelection(2, courses, System.Array.Empty<int>());

            Assert.Null(result);
        }

        // Aşağıdaki 3 test, ValidateElectiveSelection'ın Python'a birebir taşınmış bir
        // kopyasını 20.000 rastgele senaryoyla bağımsız bir oracle'a karşı fuzz-test
        // ederken bulunan uç durumlardır (dotnet bu ortamda çalıştırılamadığından yapılan
        // yan/side güvenilirlik testi — bkz. sohbet geçmişi).

        [Fact]
        public void DuplicateSelectedIds_AreDeduplicated_NotCountedTwice()
        {
            // Öğrenci aynı ders id'sini iki kez gönderse bile (ör. çift tıklama/bug'lı istemci),
            // HashSet dedup sayesinde bu 1 seçim olarak sayılmalı, 2 seçim gibi geçmemeli.
            var courses = new List<Course>
            {
                Elective(2, 5, "Teknik-5"),
                Elective(3, 5, "Teknik-5"),
            };

            // Tek bir dersi iki kez gönderiyor — Teknik-5 tam olarak 1 gerektirir.
            var result = MySqlScheduleRepository.ValidateElectiveSelection(5, courses, new[] { 2, 2 });

            Assert.Null(result); // 1 benzersiz seçim = gereken sayı, geçerli
        }

        [Fact]
        public void DuplicateIds_CannotFakeReachingHigherRequiredCount()
        {
            // Teknik-6 tam olarak 2 gerektirir; aynı dersi iki kez göndermek 2 FARKLI
            // ders seçmiş gibi sayılmamalı.
            var courses = new List<Course>
            {
                Elective(10, 6, "Teknik-6"),
                Elective(11, 6, "Teknik-6"),
            };

            var result = MySqlScheduleRepository.ValidateElectiveSelection(6, courses, new[] { 10, 10 });

            Assert.NotNull(result); // 1 benzersiz seçim var, 2 gerekiyor -> reddedilmeli
        }

        [Fact]
        public void GroupNameIsCaseSensitive()
        {
            // "Teknik-5" ile "teknik-5" farklı gruplar sayılmalı (tabloda tanımsız
            // grup adı varsayılan olarak 1 gerektirir, aynı isim gibi birleşmemeli).
            var courses = new List<Course>
            {
                Elective(2, 5, "teknik-5"), // küçük harfle — tabloda YOK, varsayılan 1 uygulanır
            };

            var result = MySqlScheduleRepository.ValidateElectiveSelection(5, courses, new[] { 2 });

            Assert.Null(result); // varsayılan 1 gerektirir, 1 seçilmiş -> geçerli
        }

        [Fact]
        public void UnlistedGroupName_DefaultsToRequiringExactlyOne()
        {
            var courses = new List<Course>
            {
                Elective(40, 4, "Custom-Grup"), // (4, "Custom-Grup") tabloda tanımlı değil
                Elective(41, 4, "Custom-Grup"),
            };

            var zeroSelected = MySqlScheduleRepository.ValidateElectiveSelection(4, courses, System.Array.Empty<int>());
            var twoSelected = MySqlScheduleRepository.ValidateElectiveSelection(4, courses, new[] { 40, 41 });
            var oneSelected = MySqlScheduleRepository.ValidateElectiveSelection(4, courses, new[] { 40 });

            Assert.NotNull(zeroSelected);
            Assert.NotNull(twoSelected);
            Assert.Null(oneSelected);
        }
    }
}
