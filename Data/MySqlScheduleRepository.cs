using GsuTimetablingSystem.Models;
using MySqlConnector;

namespace GsuTimetablingSystem.Data
{
    public class MySqlScheduleRepository
    {
        private readonly string _connectionString;

        public MySqlScheduleRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task InitializeAsync()
        {
            var builder = new MySqlConnectionStringBuilder(_connectionString);
            var databaseName = builder.Database;
            builder.Database = string.Empty;

            await using (var serverConnection = new MySqlConnection(builder.ConnectionString))
            {
                await serverConnection.OpenAsync();
                await ExecuteAsync(
                    serverConnection,
                    $"CREATE DATABASE IF NOT EXISTS `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            }

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS teachers (
                    id INT PRIMARY KEY,
                    name VARCHAR(150) NOT NULL,
                    email VARCHAR(180) NOT NULL UNIQUE,
                    password VARCHAR(120) NOT NULL
                );

                CREATE TABLE IF NOT EXISTS students (
                    id INT PRIMARY KEY,
                    student_number VARCHAR(40) NOT NULL UNIQUE,
                    name VARCHAR(150) NOT NULL,
                    email VARCHAR(180) NOT NULL UNIQUE,
                    password VARCHAR(120) NOT NULL
                );

                CREATE TABLE IF NOT EXISTS rooms (
                    id INT PRIMARY KEY,
                    name VARCHAR(100) NOT NULL,
                    capacity INT NOT NULL DEFAULT 0,
                    is_amphi BOOLEAN NOT NULL DEFAULT FALSE
                );

                CREATE TABLE IF NOT EXISTS time_slots (
                    id INT PRIMARY KEY,
                    day VARCHAR(40) NOT NULL,
                    hour_range VARCHAR(40) NOT NULL
                );

                CREATE TABLE IF NOT EXISTS courses (
                    id INT PRIMARY KEY,
                    title VARCHAR(200) NOT NULL,
                    teacher_id INT NOT NULL,
                    weekly_hours INT NOT NULL DEFAULT 2,
                    is_elective BOOLEAN NOT NULL DEFAULT FALSE,
                    elective_group VARCHAR(100) NOT NULL DEFAULT '',
                    expected_student_count INT NOT NULL DEFAULT 1,
                    CONSTRAINT fk_courses_teachers
                        FOREIGN KEY (teacher_id) REFERENCES teachers(id)
                );

                CREATE TABLE IF NOT EXISTS teacher_unavailability (
                    teacher_id INT NOT NULL,
                    time_slot_id INT NOT NULL,
                    PRIMARY KEY (teacher_id, time_slot_id),
                    CONSTRAINT fk_unavailability_teachers
                        FOREIGN KEY (teacher_id) REFERENCES teachers(id),
                    CONSTRAINT fk_unavailability_time_slots
                        FOREIGN KEY (time_slot_id) REFERENCES time_slots(id)
                );

                CREATE TABLE IF NOT EXISTS student_electives (
                    student_id INT NOT NULL,
                    course_id INT NOT NULL,
                    PRIMARY KEY (student_id, course_id),
                    CONSTRAINT fk_student_electives_students
                        FOREIGN KEY (student_id) REFERENCES students(id),
                    CONSTRAINT fk_student_electives_courses
                        FOREIGN KEY (course_id) REFERENCES courses(id)
                );

                CREATE TABLE IF NOT EXISTS manual_schedule_assignments (
                    course_id INT PRIMARY KEY,
                    room_id INT NOT NULL,
                    time_slot_id INT NOT NULL,
                    CONSTRAINT fk_manual_schedule_courses
                        FOREIGN KEY (course_id) REFERENCES courses(id)
                        ON DELETE CASCADE,
                    CONSTRAINT fk_manual_schedule_rooms
                        FOREIGN KEY (room_id) REFERENCES rooms(id),
                    CONSTRAINT fk_manual_schedule_time_slots
                        FOREIGN KEY (time_slot_id) REFERENCES time_slots(id)
                );

                CREATE TABLE IF NOT EXISTS generated_schedule_sessions (
                    course_id INT NOT NULL,
                    course_title VARCHAR(200) NOT NULL,
                    teacher_id INT NOT NULL,
                    is_elective BOOLEAN NOT NULL DEFAULT FALSE,
                    elective_group VARCHAR(100) NOT NULL DEFAULT '',
                    semester INT NOT NULL DEFAULT 0,
                    teacher_name VARCHAR(150) NOT NULL,
                    room_id INT NOT NULL,
                    room_name VARCHAR(100) NOT NULL,
                    is_amphi BOOLEAN NOT NULL DEFAULT FALSE,
                    time_slot_id INT NOT NULL,
                    duration_hours INT NOT NULL DEFAULT 1,
                    day VARCHAR(40) NOT NULL,
                    hour_range VARCHAR(40) NOT NULL,
                    is_manual BOOLEAN NOT NULL DEFAULT FALSE,
                    PRIMARY KEY (course_id, time_slot_id)
                );

                CREATE TABLE IF NOT EXISTS manual_schedule_sessions (
                    course_id INT NOT NULL,
                    room_id INT NOT NULL,
                    time_slot_id INT NOT NULL,
                    duration_hours INT NOT NULL DEFAULT 1,
                    PRIMARY KEY (course_id, time_slot_id),
                    CONSTRAINT fk_manual_schedule_sessions_courses
                        FOREIGN KEY (course_id) REFERENCES courses(id)
                        ON DELETE CASCADE,
                    CONSTRAINT fk_manual_schedule_sessions_rooms
                        FOREIGN KEY (room_id) REFERENCES rooms(id),
                    CONSTRAINT fk_manual_schedule_sessions_time_slots
                        FOREIGN KEY (time_slot_id) REFERENCES time_slots(id)
                );

                CREATE TABLE IF NOT EXISTS student_grades (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    student_id INT NOT NULL,
                    course_id INT NOT NULL,
                    grade VARCHAR(5) NOT NULL,
                    passed BOOLEAN NOT NULL DEFAULT TRUE,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE KEY unique_student_course (student_id, course_id),
                    CONSTRAINT fk_student_grades_students
                        FOREIGN KEY (student_id) REFERENCES students(id) ON DELETE CASCADE,
                    CONSTRAINT fk_student_grades_courses
                        FOREIGN KEY (course_id) REFERENCES courses(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS course_prerequisites (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    course_id INT NOT NULL,
                    prerequisite_course_id INT NOT NULL,
                    prereq_group INT NOT NULL DEFAULT 1,
                    UNIQUE KEY unique_prereq (course_id, prerequisite_course_id),
                    CONSTRAINT fk_prereq_course
                        FOREIGN KEY (course_id) REFERENCES courses(id) ON DELETE CASCADE,
                    CONSTRAINT fk_prereq_required
                        FOREIGN KEY (prerequisite_course_id) REFERENCES courses(id) ON DELETE CASCADE
                );
                """);

            if (await RequiresHourlyTimeSlotMigrationAsync(connection))
            {
                await ExecuteAsync(connection, """
                    DELETE FROM teacher_unavailability;
                    DELETE FROM manual_schedule_assignments;
                    DELETE FROM time_slots;
                    """);
            }

            if (await RequiresSemesterMigrationAsync(connection))
            {
                await ExecuteAsync(connection, """
                    DELETE FROM student_electives;
                    DELETE FROM generated_schedule_sessions;
                    DELETE FROM manual_schedule_sessions;
                    DELETE FROM manual_schedule_assignments;
                    DELETE FROM courses;
                    ALTER TABLE courses ADD COLUMN semester INT NOT NULL DEFAULT 0;
                    """);
            }

            if (await RequiresBirthDateMigrationAsync(connection))
            {
                await ExecuteAsync(connection, "ALTER TABLE students ADD COLUMN birth_date DATE NULL;");
            }

            // "Grup" (student_group / combined_group_count) kavramı sistemden tamamen kaldırıldı;
            // ders-öğrenci eşleştirmesi artık yalnızca "semester" alanına dayanıyor.
            if (await RequiresGroupRemovalMigrationAsync(connection))
            {
                await ExecuteAsync(connection, """
                    ALTER TABLE students DROP COLUMN student_group;
                    ALTER TABLE courses DROP COLUMN student_group;
                    ALTER TABLE courses DROP COLUMN combined_group_count;
                    """);
            }

            if (await RequiresScheduleSemesterMigrationAsync(connection))
            {
                await ExecuteAsync(connection, """
                    ALTER TABLE generated_schedule_sessions DROP COLUMN student_group;
                    ALTER TABLE generated_schedule_sessions ADD COLUMN semester INT NOT NULL DEFAULT 0;
                    DELETE FROM generated_schedule_sessions;
                    DELETE FROM manual_schedule_sessions;
                    """);
            }

            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS enrollments (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    student_id INT NOT NULL,
                    semester INT NOT NULL,
                    status VARCHAR(20) NOT NULL DEFAULT 'pending',
                    created_at DATETIME NOT NULL DEFAULT NOW(),
                    reviewer_note VARCHAR(500) NOT NULL DEFAULT '',
                    CONSTRAINT fk_enrollments_students
                        FOREIGN KEY (student_id) REFERENCES students(id)
                );
                """);

            // Öğrencinin "şu an kayıt talebi oluşturabileceği yarıyıl"ı tutar. Var olan
            // öğrenciler için, en son onaylanmış kayıttan bir sonraki yarıyıla göre
            // (yoksa 1. yarıyıl olarak) bir kereye mahsus geri doldurulur; sonrasında
            // yalnızca admin'in "sonraki yarıyıla ilerlet" eylemiyle değişir.
            if (await RequiresCurrentSemesterMigrationAsync(connection))
            {
                await ExecuteAsync(connection, "ALTER TABLE students ADD COLUMN current_semester INT NOT NULL DEFAULT 1;");
                await ExecuteAsync(connection, """
                    UPDATE students s
                    SET current_semester = LEAST(8, (
                        SELECT COALESCE(MAX(e.semester), 0) + 1
                        FROM enrollments e
                        WHERE e.student_id = s.id AND e.status = 'approved'
                    ))
                    WHERE EXISTS (
                        SELECT 1 FROM enrollments e
                        WHERE e.student_id = s.id AND e.status = 'approved'
                    );
                    """);
            }

            // Öğretmen girişi artık internal 'id' PK'sı yerine ayrı bir 'teacher_number' alanı
            // kullanıyor (öğrencilerin student_number'ına paralel). NULL bırakılıyor; SeedAsync
            // hemen ardından her satırı doldurur, tekil olması UNIQUE KEY ile garanti edilir.
            if (await RequiresTeacherNumberMigrationAsync(connection))
            {
                await ExecuteAsync(connection, """
                    ALTER TABLE teachers ADD COLUMN teacher_number VARCHAR(20) NULL;
                    ALTER TABLE teachers ADD UNIQUE KEY unique_teacher_number (teacher_number);
                    """);
            }

            await SeedAsync(connection);

            // Seed'de (veya daha önceki bir çalıştırmadan kalma) düz metin olarak duran
            // şifreleri PBKDF2 hash'ine çevir. Zaten hash'lenmiş satırlar dokunulmadan geçilir,
            // bu yüzden bu adım her başlangıçta güvenle (idempotent) tekrar çalıştırılabilir.
            await HashPlaintextPasswordsAsync(connection);
        }

        private static async Task HashPlaintextPasswordsAsync(MySqlConnection connection)
        {
            await HashTablePasswordsAsync(connection, "teachers");
            await HashTablePasswordsAsync(connection, "students");
        }

        private static async Task HashTablePasswordsAsync(MySqlConnection connection, string tableName)
        {
            // tableName her zaman bu dosya içinde sabit olarak ("teachers"/"students") verilir,
            // dış girdiden gelmez — SQL injection riski yoktur.
            var pending = new List<(int Id, string PlainPassword)>();

            await using (var selectCommand = new MySqlCommand(
                $"SELECT id, password FROM {tableName} WHERE password NOT LIKE 'PBKDF2$%';",
                connection))
            await using (var reader = await selectCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    pending.Add((reader.GetInt32("id"), reader.GetString("password")));
                }
            }

            foreach (var (id, plainPassword) in pending)
            {
                var hashed = PasswordHasher.Hash(plainPassword);
                await using var updateCommand = new MySqlCommand(
                    $"UPDATE {tableName} SET password = @password WHERE id = @id;",
                    connection);
                updateCommand.Parameters.AddWithValue("@password", hashed);
                updateCommand.Parameters.AddWithValue("@id", id);
                await updateCommand.ExecuteNonQueryAsync();
            }
        }

        public async Task<ScheduleData> GetScheduleDataAsync()
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var data = new ScheduleData
            {
                Teachers = await GetTeachersAsync(connection),
                Rooms = await GetRoomsAsync(connection),
                TimeSlots = await GetTimeSlotsAsync(connection),
                Courses = await GetCoursesAsync(connection)
            };

            await LoadTeacherUnavailabilityAsync(connection, data.Teachers);
            return data;
        }

        public async Task<Student?> AuthenticateStudentAsync(string studentNumber, string password)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            // Not: şifre karşılaştırması artık SQL'de değil (hash'ler her satırda farklı salt
            // içerdiğinden "password = @password" ile eşleşmez), uygulama tarafında
            // PasswordHasher.Verify ile yapılıyor.
            await using var command = new MySqlCommand(
                """
                SELECT id, student_number, name, email, password, current_semester
                FROM students
                WHERE student_number = @studentNumber;
                """,
                connection);
            command.Parameters.AddWithValue("@studentNumber", studentNumber);
            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return null;
            }

            var storedPassword = reader.GetString("password");
            if (!PasswordHasher.Verify(password, storedPassword))
            {
                return null;
            }

            return new Student
            {
                Id = reader.GetInt32("id"),
                StudentNumber = reader.GetString("student_number"),
                Name = reader.GetString("name"),
                Email = reader.GetString("email"),
                Password = string.Empty,
                CurrentSemester = reader.GetInt32("current_semester")
            };
        }

        // Öğretmenler artık e-posta değil, kendi öğretmen numaraları (teacher_number — internal
        // 'id' PK'sından ayrı, öğrencilerin student_number ile girişine paralel) ile giriş yapıyor.
        public async Task<Teacher?> AuthenticateTeacherAsync(string teacherNumber, string password)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT id, teacher_number, name, email, password FROM teachers WHERE teacher_number = @teacherNumber;",
                connection);
            command.Parameters.AddWithValue("@teacherNumber", teacherNumber);
            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return null;
            }

            var storedPassword = reader.GetString("password");
            if (!PasswordHasher.Verify(password, storedPassword))
            {
                return null;
            }

            return new Teacher
            {
                Id = reader.GetInt32("id"),
                TeacherNumber = reader.GetString("teacher_number"),
                Name = reader.GetString("name"),
                Email = reader.GetString("email"),
                Password = string.Empty
            };
        }

        public async Task<Student?> GetStudentAsync(int studentId)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT id, student_number, name, email, current_semester FROM students WHERE id = @id;",
                connection);
            command.Parameters.AddWithValue("@id", studentId);
            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new Student
            {
                Id = reader.GetInt32("id"),
                StudentNumber = reader.GetString("student_number"),
                Name = reader.GetString("name"),
                Email = reader.GetString("email"),
                CurrentSemester = reader.GetInt32("current_semester")
            };
        }

        public async Task<List<int>> GetStudentElectiveIdsAsync(int studentId)
        {
            var ids = new List<int>();
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT course_id FROM student_electives WHERE student_id = @studentId ORDER BY course_id;",
                connection);
            command.Parameters.AddWithValue("@studentId", studentId);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetInt32("course_id"));
            }

            return ids;
        }

        public async Task<List<ElectiveOption>> GetElectiveOptionsAsync(int studentId)
        {
            var student = await GetStudentAsync(studentId);
            if (student is null)
            {
                return new List<ElectiveOption>();
            }

            var selectedIds = (await GetStudentElectiveIdsAsync(studentId)).ToHashSet();
            var data = await GetScheduleDataAsync();
            var teacherById = data.Teachers.ToDictionary(teacher => teacher.Id);

            return data.Courses
                .Where(course => course.IsElective)
                .Select(course => new ElectiveOption
                {
                    CourseId = course.Id,
                    Title = course.Title,
                    ElectiveGroup = course.ElectiveGroup,
                    TeacherName = teacherById.TryGetValue(course.TeacherId, out var t) ? t.Name : "Bilinmiyor",
                    IsSelected = selectedIds.Contains(course.Id)
                })
                .ToList();
        }

        public async Task SaveStudentElectivesAsync(int studentId, IReadOnlyCollection<int> courseIds)
        {
            var options = await GetElectiveOptionsAsync(studentId);
            var allowedIds = options.Select(option => option.CourseId).ToHashSet();
            var selectedIds = courseIds.Where(allowedIds.Contains).Distinct().ToList();

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await using (var deleteCommand = new MySqlCommand(
                "DELETE FROM student_electives WHERE student_id = @studentId;",
                connection,
                transaction))
            {
                deleteCommand.Parameters.AddWithValue("@studentId", studentId);
                await deleteCommand.ExecuteNonQueryAsync();
            }

            foreach (var courseId in selectedIds)
            {
                await using var insertCommand = new MySqlCommand(
                    "INSERT INTO student_electives (student_id, course_id) VALUES (@studentId, @courseId);",
                    connection,
                    transaction);
                insertCommand.Parameters.AddWithValue("@studentId", studentId);
                insertCommand.Parameters.AddWithValue("@courseId", courseId);
                await insertCommand.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }

        public async Task SaveTeacherUnavailabilityAsync(int teacherId, IReadOnlyCollection<int> timeSlotIds)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await using (var deleteCommand = new MySqlCommand(
                "DELETE FROM teacher_unavailability WHERE teacher_id = @teacherId;",
                connection,
                transaction))
            {
                deleteCommand.Parameters.AddWithValue("@teacherId", teacherId);
                await deleteCommand.ExecuteNonQueryAsync();
            }

            foreach (var timeSlotId in timeSlotIds.Distinct())
            {
                await using var insertCommand = new MySqlCommand(
                    """
                    INSERT INTO teacher_unavailability (teacher_id, time_slot_id)
                    SELECT @teacherId, id FROM time_slots WHERE id = @timeSlotId;
                    """,
                    connection,
                    transaction);
                insertCommand.Parameters.AddWithValue("@teacherId", teacherId);
                insertCommand.Parameters.AddWithValue("@timeSlotId", timeSlotId);
                await insertCommand.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            await ClearGeneratedScheduleAsync();
        }

        public async Task<List<ScheduleResult>> GetGeneratedScheduleAsync()
        {
            var schedule = new List<ScheduleResult>();
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                """
                SELECT course_id, course_title, teacher_id, is_elective, elective_group, semester,
                       teacher_name, room_id, room_name, is_amphi, time_slot_id, duration_hours,
                       day, hour_range, is_manual
                FROM generated_schedule_sessions
                ORDER BY time_slot_id, room_id, course_id;
                """,
                connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                schedule.Add(new ScheduleResult
                {
                    CourseId = reader.GetInt32("course_id"),
                    CourseTitle = reader.GetString("course_title"),
                    TeacherId = reader.GetInt32("teacher_id"),
                    IsElective = reader.GetBoolean("is_elective"),
                    ElectiveGroup = reader.GetString("elective_group"),
                    Semester = reader.GetInt32("semester"),
                    TeacherName = reader.GetString("teacher_name"),
                    RoomId = reader.GetInt32("room_id"),
                    RoomName = reader.GetString("room_name"),
                    IsAmphi = reader.GetBoolean("is_amphi"),
                    TimeSlotId = reader.GetInt32("time_slot_id"),
                    DurationHours = reader.GetInt32("duration_hours"),
                    Day = reader.GetString("day"),
                    HourRange = reader.GetString("hour_range"),
                    IsManual = reader.GetBoolean("is_manual")
                });
            }

            return schedule;
        }

        public async Task SaveGeneratedScheduleAsync(IReadOnlyCollection<ScheduleResult> schedule)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await using (var deleteCommand = new MySqlCommand(
                "DELETE FROM generated_schedule_sessions;",
                connection,
                transaction))
            {
                await deleteCommand.ExecuteNonQueryAsync();
            }

            foreach (var item in schedule)
            {
                await using var insertCommand = new MySqlCommand(
                    """
                    INSERT INTO generated_schedule_sessions (
                        course_id, course_title, teacher_id, is_elective, elective_group, semester,
                        teacher_name, room_id, room_name, is_amphi, time_slot_id, duration_hours,
                        day, hour_range, is_manual)
                    VALUES (
                        @courseId, @courseTitle, @teacherId, @isElective, @electiveGroup, @semester,
                        @teacherName, @roomId, @roomName, @isAmphi, @timeSlotId, @durationHours,
                        @day, @hourRange, @isManual);
                    """,
                    connection,
                    transaction);
                insertCommand.Parameters.AddWithValue("@courseId", item.CourseId);
                insertCommand.Parameters.AddWithValue("@courseTitle", item.CourseTitle);
                insertCommand.Parameters.AddWithValue("@teacherId", item.TeacherId);
                insertCommand.Parameters.AddWithValue("@isElective", item.IsElective);
                insertCommand.Parameters.AddWithValue("@electiveGroup", item.ElectiveGroup);
                insertCommand.Parameters.AddWithValue("@semester", item.Semester);
                insertCommand.Parameters.AddWithValue("@teacherName", item.TeacherName);
                insertCommand.Parameters.AddWithValue("@roomId", item.RoomId);
                insertCommand.Parameters.AddWithValue("@roomName", item.RoomName);
                insertCommand.Parameters.AddWithValue("@isAmphi", item.IsAmphi);
                insertCommand.Parameters.AddWithValue("@timeSlotId", item.TimeSlotId);
                insertCommand.Parameters.AddWithValue("@durationHours", item.DurationHours);
                insertCommand.Parameters.AddWithValue("@day", item.Day);
                insertCommand.Parameters.AddWithValue("@hourRange", item.HourRange);
                insertCommand.Parameters.AddWithValue("@isManual", item.IsManual);
                await insertCommand.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }

        public async Task ClearGeneratedScheduleAsync()
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "DELETE FROM generated_schedule_sessions;",
                connection);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<Dictionary<int, List<ManualScheduleAssignment>>> GetManualScheduleAssignmentsAsync()
        {
            var assignments = new Dictionary<int, List<ManualScheduleAssignment>>();
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                """
                SELECT course_id, room_id, time_slot_id
                     , duration_hours
                FROM manual_schedule_sessions
                ORDER BY course_id, time_slot_id;
                """,
                connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var assignment = new ManualScheduleAssignment
                {
                    CourseId = reader.GetInt32("course_id"),
                    RoomId = reader.GetInt32("room_id"),
                    StartTimeSlotId = reader.GetInt32("time_slot_id"),
                    DurationHours = reader.GetInt32("duration_hours")
                };
                if (!assignments.TryGetValue(assignment.CourseId, out var courseAssignments))
                {
                    courseAssignments = new List<ManualScheduleAssignment>();
                    assignments[assignment.CourseId] = courseAssignments;
                }

                courseAssignments.Add(assignment);
            }

            return assignments;
        }

        public async Task SaveManualScheduleAssignmentsAsync(
            int courseId,
            IReadOnlyCollection<ManualScheduleAssignment> assignments)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await using (var deleteCommand = new MySqlCommand(
                "DELETE FROM manual_schedule_sessions WHERE course_id = @courseId;",
                connection,
                transaction))
            {
                deleteCommand.Parameters.AddWithValue("@courseId", courseId);
                await deleteCommand.ExecuteNonQueryAsync();
            }

            // Deduplicate by start time slot to avoid PRIMARY KEY violation
            var uniqueAssignments = assignments
                .GroupBy(a => a.StartTimeSlotId)
                .Select(g => g.First())
                .ToList();

            foreach (var assignment in uniqueAssignments)
            {
                await using var command = new MySqlCommand(
                    """
                    INSERT INTO manual_schedule_sessions (course_id, room_id, time_slot_id, duration_hours)
                    VALUES (@courseId, @roomId, @timeSlotId, @durationHours);
                    """,
                    connection,
                    transaction);
                command.Parameters.AddWithValue("@courseId", assignment.CourseId);
                command.Parameters.AddWithValue("@roomId", assignment.RoomId);
                command.Parameters.AddWithValue("@timeSlotId", assignment.StartTimeSlotId);
                command.Parameters.AddWithValue("@durationHours", assignment.DurationHours);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }

        public async Task ClearAllManualAssignmentsAsync()
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "DELETE FROM manual_schedule_sessions;",
                connection);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<List<Teacher>> GetAllTeachersAsync()
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            return await GetTeachersAsync(connection);
        }

        public async Task<Course> AddCourseAsync(Course course)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var maxCmd = new MySqlCommand("SELECT COALESCE(MAX(id), 0) FROM courses;", connection);
            course.Id = Convert.ToInt32(await maxCmd.ExecuteScalarAsync()) + 1;

            await using var cmd = new MySqlCommand(
                """
                INSERT INTO courses
                    (id, title, teacher_id, weekly_hours, is_elective, elective_group,
                     expected_student_count, semester)
                VALUES
                    (@id, @title, @teacherId, @weeklyHours, @isElective, @electiveGroup,
                     @expectedStudentCount, @semester);
                """,
                connection);
            cmd.Parameters.AddWithValue("@id", course.Id);
            cmd.Parameters.AddWithValue("@title", course.Title);
            cmd.Parameters.AddWithValue("@teacherId", course.TeacherId);
            cmd.Parameters.AddWithValue("@weeklyHours", course.WeeklyHours);
            cmd.Parameters.AddWithValue("@isElective", course.IsElective);
            cmd.Parameters.AddWithValue("@electiveGroup", course.ElectiveGroup);
            cmd.Parameters.AddWithValue("@expectedStudentCount", course.ExpectedStudentCount);
            cmd.Parameters.AddWithValue("@semester", course.Semester);
            await cmd.ExecuteNonQueryAsync();
            return course;
        }

        public async Task DeleteCourseAsync(int courseId)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            foreach (var sql in new[]
            {
                "DELETE FROM student_electives WHERE course_id = @id;",
                "DELETE FROM generated_schedule_sessions WHERE course_id = @id;",
                "DELETE FROM courses WHERE id = @id;"
            })
            {
                await using var cmd = new MySqlCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@id", courseId);
                await cmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }

        public async Task ClearManualScheduleAssignmentAsync(int courseId)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "DELETE FROM manual_schedule_sessions WHERE course_id = @courseId;",
                connection);
            command.Parameters.AddWithValue("@courseId", courseId);
            await command.ExecuteNonQueryAsync();
        }

        // ── Prerequisite management (teacher-owned) ───────────────────────────

        public async Task<List<Course>> GetCoursesByTeacherAsync(int teacherId)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                """
                SELECT id, title, teacher_id, weekly_hours, is_elective, elective_group,
                       expected_student_count, semester
                FROM courses
                WHERE teacher_id = @teacherId
                ORDER BY semester, title;
                """,
                connection);
            command.Parameters.AddWithValue("@teacherId", teacherId);

            var list = new List<Course>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new Course
                {
                    Id = reader.GetInt32("id"),
                    Title = reader.GetString("title"),
                    TeacherId = reader.GetInt32("teacher_id"),
                    WeeklyHours = reader.GetInt32("weekly_hours"),
                    IsElective = reader.GetBoolean("is_elective"),
                    ElectiveGroup = reader.GetString("elective_group"),
                    ExpectedStudentCount = reader.GetInt32("expected_student_count"),
                    Semester = reader.GetInt32("semester")
                });
            }
            return list;
        }

        /// <summary>Returns true if the given course exists and is taught by teacherId.</summary>
        public async Task<bool> IsCourseOwnedByTeacherAsync(int teacherId, int courseId)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM courses WHERE id = @courseId AND teacher_id = @teacherId;",
                connection);
            cmd.Parameters.AddWithValue("@courseId", courseId);
            cmd.Parameters.AddWithValue("@teacherId", teacherId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        public async Task<List<Course>> GetAllCoursesBasicAsync()
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            return await GetCoursesAsync(connection);
        }

        public async Task<List<CoursePrerequisiteRecord>> GetPrerequisitesForTeacherAsync(int teacherId)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                """
                SELECT cp.id, cp.course_id, c.title AS course_title,
                       cp.prerequisite_course_id, pc.title AS prereq_title, cp.prereq_group
                FROM course_prerequisites cp
                JOIN courses c  ON c.id  = cp.course_id
                JOIN courses pc ON pc.id = cp.prerequisite_course_id
                WHERE c.teacher_id = @teacherId
                ORDER BY c.semester, c.title, cp.prereq_group;
                """,
                connection);
            cmd.Parameters.AddWithValue("@teacherId", teacherId);

            var list = new List<CoursePrerequisiteRecord>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new CoursePrerequisiteRecord
                {
                    Id = reader.GetInt32("id"),
                    CourseId = reader.GetInt32("course_id"),
                    CourseTitle = reader.GetString("course_title"),
                    PrerequisiteCourseId = reader.GetInt32("prerequisite_course_id"),
                    PrerequisiteCourseTitle = reader.GetString("prereq_title"),
                    PrereqGroup = reader.GetInt32("prereq_group")
                });
            }
            return list;
        }

        /// <summary>
        /// Adds a prerequisite relationship. Returns null if the course does not
        /// belong to the given teacher (ownership check), so the caller can 403.
        /// </summary>
        public async Task<CoursePrerequisiteRecord?> AddPrerequisiteAsync(
            int teacherId, int courseId, int prerequisiteCourseId, int prereqGroup)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await using (var ownCmd = new MySqlCommand(
                "SELECT COUNT(*) FROM courses WHERE id = @courseId AND teacher_id = @teacherId;",
                connection))
            {
                ownCmd.Parameters.AddWithValue("@courseId", courseId);
                ownCmd.Parameters.AddWithValue("@teacherId", teacherId);
                var owns = Convert.ToInt32(await ownCmd.ExecuteScalarAsync()) > 0;
                if (!owns) return null;
            }

            await using (var insertCmd = new MySqlCommand(
                """
                INSERT IGNORE INTO course_prerequisites (course_id, prerequisite_course_id, prereq_group)
                VALUES (@courseId, @prereqId, @group);
                """,
                connection))
            {
                insertCmd.Parameters.AddWithValue("@courseId", courseId);
                insertCmd.Parameters.AddWithValue("@prereqId", prerequisiteCourseId);
                insertCmd.Parameters.AddWithValue("@group", prereqGroup);
                await insertCmd.ExecuteNonQueryAsync();
            }

            await using var getCmd = new MySqlCommand(
                """
                SELECT cp.id, cp.course_id, c.title AS course_title,
                       cp.prerequisite_course_id, pc.title AS prereq_title, cp.prereq_group
                FROM course_prerequisites cp
                JOIN courses c  ON c.id  = cp.course_id
                JOIN courses pc ON pc.id = cp.prerequisite_course_id
                WHERE cp.course_id = @courseId AND cp.prerequisite_course_id = @prereqId;
                """,
                connection);
            getCmd.Parameters.AddWithValue("@courseId", courseId);
            getCmd.Parameters.AddWithValue("@prereqId", prerequisiteCourseId);
            await using var reader = await getCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new CoursePrerequisiteRecord
            {
                Id = reader.GetInt32("id"),
                CourseId = reader.GetInt32("course_id"),
                CourseTitle = reader.GetString("course_title"),
                PrerequisiteCourseId = reader.GetInt32("prerequisite_course_id"),
                PrerequisiteCourseTitle = reader.GetString("prereq_title"),
                PrereqGroup = reader.GetInt32("prereq_group")
            };
        }

        /// <summary>Deletes a prerequisite row only if it belongs to a course owned by this teacher.</summary>
        public async Task<bool> DeletePrerequisiteAsync(int teacherId, int id)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                """
                DELETE cp FROM course_prerequisites cp
                JOIN courses c ON c.id = cp.course_id
                WHERE cp.id = @id AND c.teacher_id = @teacherId;
                """,
                connection);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@teacherId", teacherId);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        // ── Student registration (admin) ──────────────────────────────────────

        /// <summary>
        /// Registers a new student, auto-generating a login email from the name
        /// (ogr.gsu.edu.tr convention) and a password from the birth date (dd/MM/yyyy).
        /// Returns the created student plus the plaintext generated password
        /// (only ever available here, right after creation).
        /// </summary>
        public async Task<(Student Student, string PlainPassword)> RegisterStudentAsync(
            string name, string studentNumber, DateOnly birthDate)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var baseSlug = SlugifyName(name);
            if (string.IsNullOrEmpty(baseSlug)) baseSlug = "ogrenci";

            var email = $"{baseSlug}@ogr.gsu.edu.tr";
            var suffix = 1;
            while (await EmailExistsAsync(connection, email))
            {
                suffix++;
                email = $"{baseSlug}{suffix}@ogr.gsu.edu.tr";
            }

            var password = birthDate.ToString("dd/MM/yyyy");
            // Veritabanına düz metin değil, hash'i yazılır; öğrenciye/gösterime dönen
            // GeneratedPassword (aşağıdaki return) hâlâ gerçek (plaintext) şifredir —
            // bu tek seferlik gösterim için gereklidir ve depolanan değeri etkilemez.
            var hashedPassword = PasswordHasher.Hash(password);

            await using var maxCmd = new MySqlCommand("SELECT COALESCE(MAX(id), 0) FROM students;", connection);
            var id = Convert.ToInt32(await maxCmd.ExecuteScalarAsync()) + 1;

            await using var cmd = new MySqlCommand(
                """
                INSERT INTO students (id, student_number, name, email, password, birth_date)
                VALUES (@id, @studentNumber, @name, @email, @password, @birthDate);
                """,
                connection);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@studentNumber", studentNumber);
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@email", email);
            cmd.Parameters.AddWithValue("@password", hashedPassword);
            cmd.Parameters.AddWithValue("@birthDate", birthDate.ToDateTime(TimeOnly.MinValue));
            await cmd.ExecuteNonQueryAsync();

            var student = new Student
            {
                Id = id,
                StudentNumber = studentNumber,
                Name = name,
                Email = email,
                Password = string.Empty,
                BirthDate = birthDate
            };
            return (student, password);
        }

        private static async Task<bool> EmailExistsAsync(MySqlConnection connection, string email)
        {
            await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM students WHERE email = @email;", connection);
            cmd.Parameters.AddWithValue("@email", email);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        private static string SlugifyName(string fullName)
        {
            // Türkçe karakterleri ASCII karşılıklarına çevirip e-posta uyumlu hale getirir.
            var replacements = new Dictionary<char, char>
            {
                ['ç'] = 'c', ['Ç'] = 'c', ['ğ'] = 'g', ['Ğ'] = 'g',
                ['ı'] = 'i', ['I'] = 'i', ['İ'] = 'i',
                ['ö'] = 'o', ['Ö'] = 'o', ['ş'] = 's', ['Ş'] = 's',
                ['ü'] = 'u', ['Ü'] = 'u'
            };

            var sb = new System.Text.StringBuilder();
            foreach (var ch in fullName.Trim())
            {
                if (replacements.TryGetValue(ch, out var replaced))
                {
                    sb.Append(replaced);
                }
                else if (char.IsWhiteSpace(ch))
                {
                    sb.Append('.');
                }
                else if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToLowerInvariant(ch));
                }
                // punctuation (apostrophes, hyphens, dots) is dropped
            }

            var slug = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "\\.+", ".").Trim('.');
            return slug;
        }

        private static async Task SeedAsync(MySqlConnection connection)
        {
            await ExecuteAsync(connection, """
                -- teacher_number: öğretmenin GİRİŞ İÇİN kullandığı numara (internal 'id' PK'sından ayrı,
                -- öğrencilerin student_number'ına paralel bir tasarım). Format: 1000 + id, sabit 4 hane.
                INSERT INTO teachers (id, teacher_number, name, email, password) VALUES
                    (1,  '1001', 'Prof. Dr. Gülfem Alptekin',           'gulfem.alptekin@gsu.edu.tr',           '1234'),
                    (2,  '1002', 'Prof. Dr. Tankut Acarman',           'tankut.acarman@gsu.edu.tr',           '1234'),
                    (3,  '1003', 'Doç. Dr. Günce Keziban Orman',       'gunce.keziban.orman@gsu.edu.tr',       '1234'),
                    (4,  '1004', 'Dr. Öğr. Üyesi Murat Akın',          'murat.akin@gsu.edu.tr',               '1234'),
                    (5,  '1005', 'Dr. Öğr. Üyesi Reis Burak Arslan',   'reis.burak.arslan@gsu.edu.tr',        '1234'),
                    (6,  '1006', 'Dr. Öğr. Üyesi Uzay Çetin',          'uzay.cetin@gsu.edu.tr',               '1234'),
                    (7,  '1007', 'Dr. Öğr. Üyesi Serhan Daniş',        'serhan.danis@gsu.edu.tr',             '1234'),
                    (8,  '1008', 'Dr. Öğr. Üyesi Ayşegül Tüysüz Erman','aysegul.tuysuz.erman@gsu.edu.tr',     '1234'),
                    (9,  '1009', 'Dr. Öğr. Üyesi İlknur Erol',         'ilknur.erol@gsu.edu.tr',              '1234'),
                    (10, '1010', 'Dr. Öğr. Üyesi Ahmet Teoman Naskali','ahmet.teoman.naskali@gsu.edu.tr',     '1234'),
                    (11, '1011', 'Dr. Öğr. Üyesi Burak Parlak',        'burak.parlak@gsu.edu.tr',             '1234'),
                    (12, '1012', 'Dr. Öğr. Üyesi Özgün Pınarer',       'ozgun.pinarer@gsu.edu.tr',            '1234'),
                    (13, '1013', 'Dr. Öğr. Üyesi Erden Tuğcu',         'erden.tugcu@gsu.edu.tr',              '1234'),
                    (14, '1014', 'Dr. Öğr. Üyesi Pınar Uluer',         'pinar.uluer@gsu.edu.tr',              '1234'),
                    (15, '1015', 'Öğr. Gör. Dr. Tamer Özyiğit',        'tamer.ozyigit@gsu.edu.tr',            '1234'),
                    (16, '1016', 'Öğr. Gör. Dr. Marie-Christine Peroueme','marie-christine.peroueme@gsu.edu.tr','1234'),
                    (17, '1017', 'Öğr. Gör. Dr. Sultan N. Turhan',     'sultan.turhan@gsu.edu.tr',            '1234'),
                    (18, '1018', 'Öğr. Gör. Damien Berthet',           'damien.berthet@gsu.edu.tr',           '1234'),
                    (19, '1019', 'Arş. Gör. Mustafa Berk Bacaksız',    'mustafa.berk.bacaksiz@gsu.edu.tr',    '1234'),
                    (20, '1020', 'Arş. Gör. Eda Bahar',                'eda.bahar@gsu.edu.tr',                '1234'),
                    (21, '1021', 'Arş. Gör. İsmail Ozan Çelikel',      'ismail.ozan.celikel@gsu.edu.tr',      '1234'),
                    (22, '1022', 'Arş. Gör. Münire Gülru Dedeoğlu',    'munire.gulru.dedeoglu@gsu.edu.tr',    '1234'),
                    (23, '1023', 'Arş. Gör. Timoteos Onur Özçelik',    'timoteos.onur.ozcelik@gsu.edu.tr',    '1234'),
                    (24, '1024', 'Arş. Gör. Şükrü Demir İnan Özer',    'sukru.demir.inan.ozer@gsu.edu.tr',    '1234'),
                    (25, '1025', 'Arş. Gör. Musa Şervan Şahin',        'musa.servan.sahin@gsu.edu.tr',        '1234'),
                    (26, '1026', 'Arş. Gör. Abdülkadir Hazar Ünal',    'abdulkadir.hazar.unal@gsu.edu.tr',    '1234'),
                    (27, '1027', 'Arş. Gör. Batuhan Yılmaz',           'batuhan.yilmaz@gsu.edu.tr',           '1234'),
                    (28, '1028', 'Fatma Ayfer Karayel',                'ayfer.karayel@gsu.edu.tr',            '1234'),
                    (29, '1029', 'Osman Olcay Kunal',                  'olcay.kunal@gsu.edu.tr',              '1234'),
                    -- Aşağıdaki 4 kişi ects.gsu.edu.tr ders detay sayfalarındaki ("Dersi Veren(ler)")
                    -- doğrulaması sırasında bulundu, önceki kadroda yoktu:
                    (30, '1030', 'Öğr. Gör. Esin Mukul Taylan',        'emukul@gsu.edu.tr',                   '1234'),
                    (31, '1031', 'Dr. Öğr. Üyesi Mouloud Adel',        'madel@gsu.edu.tr',                    '1234'),
                    (32, '1032', 'Öğr. Gör. Burak Arslan',             'ext-gsu@burakarslan.com',             '1234'),  -- DİKKAT: id5 'Reis Burak Arslan'dan FARKLI kişi (ECTS'de ayrı e-posta ile kayıtlı)
                    (33, '1033', 'Öğr. Gör. Zübeyde Gaye Çankaya Eksen','gayecankaya@yahoo.com',              '1234')
                ON DUPLICATE KEY UPDATE
                    teacher_number = VALUES(teacher_number), name = VALUES(name), email = VALUES(email);
                    -- NOT: password kasıtlı olarak burada güncellenmiyor; ilk kurulumda seed'deki
                    -- düz metin değer yazılır ve HashPlaintextPasswordsAsync tarafından hash'e
                    -- çevrilir, sonraki her yeniden başlatmada bu satır tekrar plaintext'e dönmesin
                    -- diye password sütunu bu UPDATE'in dışında bırakıldı.

                -- Sifreler dogum tarihinden uretilir (gg/aa/yyyy formati). Ilk kurulumda duz metin
                -- yazilir, HashPlaintextPasswordsAsync tarafindan hash'e cevrilir (asagidaki NOT
                -- ile ayni sebepten password bu UPDATE'in disinda tutuluyor).
                -- current_semester: en son onaylı kayıttan (aşağıdaki enrollments seed'i) bir sonraki yarıyıl.
                -- Yalnızca ilk oluşturmada ayarlanır (ON DUPLICATE KEY UPDATE'te yok) — sonrasında admin'in
                -- "sonraki yarıyıla ilerlet" eylemiyle değişir, her başlangıçta sıfırlanmaz.
                INSERT INTO students (id, student_number, name, email, password, birth_date, current_semester) VALUES
                    (1, '2024001', 'Deniz Yilmaz', 'deniz.yilmaz@ogr.gsu.edu.tr', '14/05/2006', '2006-05-14', 2),
                    (2, '2024002', 'Ece Kaya', 'ece.kaya@ogr.gsu.edu.tr', '22/09/2006', '2006-09-22', 2),
                    (3, '2023001', 'Mert Aydin', 'mert.aydin@ogr.gsu.edu.tr', '03/11/2005', '2005-11-03', 4),
                    (4, '2023002', 'Selin Demir', 'selin.demir@ogr.gsu.edu.tr', '27/02/2005', '2005-02-27', 4),
                    (5, '2022001', 'Ada Arslan', 'ada.arslan@ogr.gsu.edu.tr', '19/07/2004', '2004-07-19', 6),
                    (6, '2022002', 'Emir Koc', 'emir.koc@ogr.gsu.edu.tr', '08/12/2004', '2004-12-08', 6),
                    (7, '2021001', 'Lara Sahin', 'lara.sahin@ogr.gsu.edu.tr', '11/04/2003', '2003-04-11', 8),
                    (8, '2021002', 'Kerem Eren', 'kerem.eren@ogr.gsu.edu.tr', '30/01/2003', '2003-01-30', 8)
                ON DUPLICATE KEY UPDATE
                    name = VALUES(name), email = VALUES(email),
                    birth_date = VALUES(birth_date);

                INSERT INTO rooms (id, name, capacity, is_amphi) VALUES
                    (1, 'D201', 40, FALSE),
                    (2, 'D202', 45, FALSE),
                    (3, 'LAB1', 30, FALSE),
                    (4, 'LAB2', 30, FALSE),
                    (5, 'Amfi A', 140, TRUE),
                    (6, 'Amfi B', 120, TRUE)
                ON DUPLICATE KEY UPDATE
                    name = VALUES(name), capacity = VALUES(capacity), is_amphi = VALUES(is_amphi);

                INSERT INTO time_slots (id, day, hour_range) VALUES
                    (1,  'Pazartesi', '09:00-10:00'),
                    (2,  'Pazartesi', '10:00-11:00'),
                    (3,  'Pazartesi', '11:00-12:00'),
                    (4,  'Pazartesi', '12:00-13:00'),
                    (5,  'Pazartesi', '13:00-14:00'),
                    (6,  'Pazartesi', '14:00-15:00'),
                    (7,  'Pazartesi', '15:00-16:00'),
                    (8,  'Pazartesi', '16:00-17:00'),
                    (9,  'Pazartesi', '17:00-18:00'),
                    (10, 'Sali',      '09:00-10:00'),
                    (11, 'Sali',      '10:00-11:00'),
                    (12, 'Sali',      '11:00-12:00'),
                    (13, 'Sali',      '12:00-13:00'),
                    (14, 'Sali',      '13:00-14:00'),
                    (15, 'Sali',      '14:00-15:00'),
                    (16, 'Sali',      '15:00-16:00'),
                    (17, 'Sali',      '16:00-17:00'),
                    (18, 'Sali',      '17:00-18:00'),
                    (19, 'Carsamba',  '09:00-10:00'),
                    (20, 'Carsamba',  '10:00-11:00'),
                    (21, 'Carsamba',  '11:00-12:00'),
                    (22, 'Carsamba',  '12:00-13:00'),
                    (23, 'Carsamba',  '13:00-14:00'),
                    (24, 'Carsamba',  '14:00-15:00'),
                    (25, 'Carsamba',  '15:00-16:00'),
                    (26, 'Carsamba',  '16:00-17:00'),
                    (27, 'Carsamba',  '17:00-18:00'),
                    (28, 'Persembe',  '09:00-10:00'),
                    (29, 'Persembe',  '10:00-11:00'),
                    (30, 'Persembe',  '11:00-12:00'),
                    (31, 'Persembe',  '12:00-13:00'),
                    (32, 'Persembe',  '13:00-14:00'),
                    (33, 'Persembe',  '14:00-15:00'),
                    (34, 'Persembe',  '15:00-16:00'),
                    (35, 'Persembe',  '16:00-17:00'),
                    (36, 'Persembe',  '17:00-18:00'),
                    (37, 'Cuma',      '09:00-10:00'),
                    (38, 'Cuma',      '10:00-11:00'),
                    (39, 'Cuma',      '11:00-12:00'),
                    (40, 'Cuma',      '12:00-13:00'),
                    (41, 'Cuma',      '13:00-14:00'),
                    (42, 'Cuma',      '14:00-15:00'),
                    (43, 'Cuma',      '15:00-16:00'),
                    (44, 'Cuma',      '16:00-17:00'),
                    (45, 'Cuma',      '17:00-18:00')
                ON DUPLICATE KEY UPDATE day = VALUES(day), hour_range = VALUES(hour_range);

                -- id, title, teacher_id, weekly_hours, is_elective, elective_group, expected_student_count, semester
                INSERT INTO courses (id, title, teacher_id, weekly_hours, is_elective, elective_group, expected_student_count, semester) VALUES
                    -- 1. Yarıyıl  (weekly_hours = Teori+Uygulama+Lab, kaynak: ects.gsu.edu.tr)
                    (1,  'ING106 Matematik I',                                  18, 6, FALSE, '', 80, 1),  -- T4+U2 (Damien Berthet, ects.gsu.edu.tr)
                    (2,  'ING116 Fizik I',                                       13, 5, FALSE, '', 80, 1),  -- T3+L2 (Erden Tuğcu)
                    (3,  'ING111 Ekonominin Temelleri',                          28, 3, FALSE, '', 80, 1),  -- T3 (Fatma Ayfer Karayel)
                    (4,  'INF112 Programlamaya Giriş',                           10, 4, FALSE, '', 80, 1),  -- T2+L2 (Ahmet Teoman Naskali)
                    (5,  'INF113 Bilgisayar Mühendisliğine Giriş',                8, 3, FALSE, '', 80, 1),  -- T2+U1 (Ayşegül Tüysüz Erman)
                    (6,  'ATA001 Atatürk İlkeleri ve İnkılap Tarihi I',         26, 2, FALSE, '', 80, 1),  -- ortak ders
                    (7,  'TUR001 Türk Dili I',                                   17, 2, FALSE, '', 80, 1),  -- ortak ders
                    (8,  'FLF101 Fransızca CEF B2.1 Akademik',                   29, 4, FALSE, '', 80, 1),  -- T4 (Osman Olcay Kunal)
                    (9,  'Yabancı Dil I',                                         18, 2, FALSE, '', 80, 1),  -- ortak ders
                    -- 2. Yarıyıl
                    (10, 'ING107 Matematik II',                                  16, 6, FALSE, '', 80, 2),  -- T4+U2 (Marie-Christine Peroueme)
                    (11, 'ING117 Fizik II',                                       13, 5, FALSE, '', 80, 2),  -- T3+L2 (Erden Tuğcu)
                    (12, 'INF114 İleri Bilgisayar Programlama',                  14, 4, FALSE, '', 80, 2),  -- T2+L2 (Pınar Uluer)
                    (13, 'INF116 Bilgisayar Sistemlerine Giriş',                 10, 3, FALSE, '', 80, 2),  -- T3 (Ahmet Teoman Naskali)
                    (14, 'ATA002 Atatürk İlkeleri ve İnkılap Tarihi II',        26, 2, FALSE, '', 80, 2),  -- ortak ders
                    (15, 'CNT120 Girişimcilik ve Kariyer Planlama',              12, 2, FALSE, '', 80, 2),  -- T1+U1 (Özgün Pınarer)
                    (16, 'TUR002 Türk Dili II',                                   17, 2, FALSE, '', 80, 2),  -- ortak ders
                    (17, 'FLF201 Fransızca CEF B2.2 Akademik',                   29, 4, FALSE, '', 80, 2),  -- T4 (Osman Olcay Kunal)
                    (18, 'Yabancı Dil II',                                         18, 2, FALSE, '', 80, 2),  -- ortak ders
                    -- 3. Yarıyıl  (11.07.2026 ects.gsu.edu.tr/tr/program/programmedetails/12?ayid=36 + her dersin
                    -- kendi coursedetails sayfasındaki "Dersi Veren(ler)" alanı ile doğrulandı)
                    (20, 'ING207 Lineer Cebir',                                   16, 4, FALSE, '', 60, 3),  -- T2+U2 (Marie-Christine Peroueme) — eski kayıtta kod yanlışlıkla 'INF207', öğretmen yanlıştı
                    (21, 'INF256 Olasılık',                                       15, 3, FALSE, '', 60, 3),  -- T3 (Tamer Özyiğit) — eski kayıtta öğretmen yanlıştı
                    (23, 'INF224 Veri Yapısı ve Algoritmalar',                    3, 4, FALSE, '', 60, 3),  -- T2+L2 (Günce Keziban Orman, prereq INF112/INF114)
                    (24, 'ING229 Analog Elektronik',                              13, 6, FALSE, '', 60, 3),  -- T2+U2+L2 (Erden Tuğcu) — eski kayıtta öğretmen yanlıştı
                    (25, 'Sosyal Seçmeli Ders (3. Yarıyıl)',                     26, 2, TRUE,  'Sosyal-3', 30, 3),  -- grup dersi, tek öğretmen yok
                    (26, 'Yabancı Dil III',                                        18, 2, FALSE, '', 60, 3),  -- ortak ders
                    -- 4. Yarıyıl
                    (28, 'ING208 Diferansiyel Denklemler',                        18, 3, FALSE, '', 60, 4),  -- T2+U1 (Damien Berthet) — eski kayıtta adı 'Sayısal Yöntemler', öğretmen yanlıştı
                    (29, 'INF257 İstatistik ve Veri Analizi',                     3, 3, FALSE, '', 60, 4),  -- T3 (Günce Keziban Orman) — eski kayıtta öğretmen yanlıştı
                    (30, 'ING220 Sayısal Elektronik',                             4, 4, FALSE, '', 60, 4),  -- T2+L2 (Murat Akın; ortak: Reis Burak Arslan) — eski kayıtta öğretmen yanlıştı
                    (31, 'INF243 Nesneye Yönelik Programlama',                    8, 4, FALSE, '', 60, 4),  -- T2+L2 (Ayşegül Tüysüz Erman, prereq INF114) — eski kayıtta öğretmen yanlıştı
                    (32, 'INF291 Staj I',                                          4, 2, FALSE, '', 60, 4),  -- L2 (Murat Akın) — eski kayıtta öğretmen yanlıştı
                    (33, 'Yabancı Dil IV',                                         18, 2, FALSE, '', 60, 4),  -- ortak ders
                    -- 5. Yarıyıl
                    (19, 'INF315 Kesikli Matematik',                              12, 3, FALSE, '', 40, 5),  -- T3 (Özgün Pınarer) — eski kayıtta yanlışlıkla 3.yy'da, kod 'INF215', öğretmen yanlıştı
                    (22, 'CNT250 Proje, Risk ve Değişiklik Yönetimi',            30, 2, FALSE, '', 40, 5),  -- T2 (Esin Mukul Taylan) — eski kayıtta yanlışlıkla 3.yy'daydı, öğretmen yanlıştı
                    (35, 'INF324 Veri Tabanı Tasarımı ve Uygulamaları',          17, 4, FALSE, '', 40, 5),  -- T2+L2 (Sultan Nezihe Turhan) — eski kayıtta öğretmen yanlıştı
                    (37, 'INF345 Sayısal Sinyal İşleme',                          31, 3, FALSE, '', 40, 5),  -- T3 (Mouloud Adel) — eski kayıtta öğretmen yanlıştı
                    (38, 'INF320 Bilgisayar Mimarisi',                            4, 3, FALSE, '', 40, 5),  -- T3 (Murat Akın, prereq ING220) — eski kayıtta öğretmen yanlıştı
                    (39, 'INF353 Web Programlama',                                 12, 3, TRUE,  'Teknik-5', 20, 5),  -- T3 (Özgün Pınarer) — eski kayıtta öğretmen yanlıştı
                    (40, 'INF354 Bilişimde Oyun Teorisi ve Uygulamaları',          15, 3, TRUE,  'Teknik-5', 20, 5),  -- T3 (Tamer Özyiğit) — eski kayıtta öğretmen yanlıştı
                    (41, 'INF454 İnsan Bilgisayar Etkileşiminin Temelleri',       5, 3, TRUE,  'Teknik-5', 20, 5),  -- T3 (Reis Burak Arslan) — eski kayıtta yanlışlıkla 7.yy'da, kod 'INF383', öğretmen yanlıştı
                    (42, 'INF321 Teknik Resim',                                    15, 2, TRUE,  'Teknik-5', 20, 5),  -- ECTS Not 5: 5.yy'a bağlı denklik dersi, kendi coursedetails sayfası yok -- öğretmen ataması doğrulanamadı
                    (43, 'Yabancı Dil V',                                           18, 2, FALSE, '', 40, 5),  -- ortak ders
                    (74, 'INF356 Veri Analizine Giriş',                            3, 3, FALSE, '', 40, 5),  -- T3 (Günce Keziban Orman, prereq INF256/INF257)
                    -- 6. Yarıyıl
                    (34, 'INF334 Bilgisayar Ağları',                              2, 4, FALSE, '', 40, 6),  -- T2+L2 (Tankut Acarman, prereq INF256/INF257) — eski kayıtta yanlışlıkla 5.yy'daydı
                    (27, 'INF323 Otomatlar ve Diller Teorisi',                    12, 3, FALSE, '', 40, 6),  -- T3 (Özgün Pınarer) — eski kayıtta yanlışlıkla 4.yy'da, kod 'INF225', öğretmen yanlıştı
                    (44, 'INF333 İşletim Sistemleri',                              32, 4, FALSE, '', 40, 6),  -- T2+L2 (Burak Arslan — id5'ten farklı kişi, prereq INF116) — eski kayıtta öğretmen yanlıştı
                    (46, 'INF340 Mikroişlemciler',                                 5, 4, FALSE, '', 40, 6),  -- T2+L2 (Reis Burak Arslan) — eski kayıtta öğretmen yanlıştı
                    (48, 'INF330 Robotik',                                         14, 3, TRUE,  'Teknik-6', 20, 6),  -- T3 (Pınar Uluer) — eski kayıtta öğretmen yanlıştı
                    (49, 'INF360 Veri Tabanı Yönetimi ve Güvenliği',              17, 3, TRUE,  'Teknik-6', 20, 6),  -- T3 (Sultan Nezihe Turhan, prereq INF324) — eski kayıtta öğretmen yanlıştı
                    (50, 'INF365 Haberleşme ve Multimedya',                       11, 3, TRUE,  'Teknik-6', 20, 6),  -- T3 (Burak Parlak) — eski kayıtta öğretmen yanlıştı
                    (51, 'INF366 Sayısal Görüntü İşleme',                          31, 3, TRUE,  'Teknik-6', 20, 6),  -- T3 (Mouloud Adel) — eski kayıtta öğretmen yanlıştı
                    (53, 'INF399 Staj II',                                          4, 2, FALSE, '', 40, 6),  -- L2 (Murat Akın, prereq INF291) — eski kayıtta öğretmen yanlıştı
                    (75, 'INF325 Sayısal Analiz',                                   11, 3, FALSE, '', 40, 6),  -- T3 (Burak Parlak, prereq ING207)
                    -- 7. Yarıyıl
                    (54, 'INF493 Bilgisayar Müh. Araştırma Konuları',             6, 3, FALSE, '', 30, 7),  -- T3 (Uzay Çetin) — eski kayıtta öğretmen yanlıştı
                    (36, 'INF444 Yapay Zeka',                                      14, 3, FALSE, '', 30, 7),  -- T3 (Pınar Uluer, prereq INF224) — eski kayıtta yanlışlıkla 5.yy'da, kod 'INF344', öğretmen yanlıştı
                    (56, 'INF471 Bilişimde Güvenlik',                             4, 4, FALSE, '', 15, 7),  -- T2+L2 (Murat Akın, prereq INF334) — ECTS'de Zorunlu; eski kayıtta yanlışlıkla seçmeliydi, öğretmen yanlıştı
                    (58, 'INF400 Veri Derlemesi',                                   32, 3, FALSE, '', 15, 7),  -- T3 (Burak Arslan — id5'ten farklı kişi, prereq INF114) — ECTS'de Zorunlu; eski kayıtta yanlışlıkla seçmeliydi, adı 'Derleyici Tasarımı' ve öğretmen yanlıştı
                    (60, 'INF443 Dağıtık Sistemler ve Uygulamalar',              6, 3, FALSE, '', 15, 7),  -- T3 (Uzay Çetin, prereq INF114/INF243) — ECTS'de Zorunlu; eski kayıtta yanlışlıkla seçmeliydi, öğretmen yanlıştı
                    (52, 'INF402 Nesnelerin İnternetine Giriş',                   12, 4, FALSE, '', 15, 7),  -- T2+L2 (Özgün Pınarer) — eski kayıtta yanlışlıkla 6.yy'da, seçmeli, kod 'INF302' ve öğretmen yanlıştı
                    (57, 'INF438 İleri Veri Tabanları',                            17, 3, TRUE,  'Teknik-7', 15, 7),  -- T3 (Sultan Nezihe Turhan, prereq INF324) — eski kayıtta öğretmen yanlıştı
                    (59, 'INF410 Medical Informatics',                              5, 3, TRUE,  'Teknik-7', 15, 7),  -- T3 (Reis Burak Arslan) — eski kayıtta öğretmen yanlıştı
                    (61, 'INF432 Bilgisayar Grafikleri',                          11, 3, TRUE,  'Teknik-7', 15, 7),  -- T3 (Burak Parlak) — eski kayıtta öğretmen yanlıştı
                    (63, 'Sosyal Seçmeli Ders (7. Yarıyıl)',                     26, 2, TRUE,  'Sosyal-7', 15, 7),  -- grup dersi, tek öğretmen yok
                    (64, 'Yabancı Dil VII',                                         18, 2, FALSE, '', 30, 7),  -- ortak ders
                    -- 8. Yarıyıl
                    (65, 'INF494 Bitirme Projesi',                                  1, 3, FALSE, '', 30, 8),  -- U3 (Gülfem Alptekin, prereq INF493) — eski kayıtta öğretmen yanlıştı
                    (45, 'INF482 Gömülü Sistem Tasarım Temelleri',                10, 4, FALSE, '', 30, 8),  -- T4 (Ahmet Teoman Naskali) — eski kayıtta yanlışlıkla 6.yy'da, kod 'INF382' idi
                    (47, 'INF481 Yazılım Mühendisliği ve Nesneye Yönelik Tasarım', 1, 4, FALSE, '', 30, 8),  -- T4 (Gülfem Alptekin) — eski kayıtta yanlışlıkla 6.yy'da, kod 'INF381' ve öğretmen yanlıştı
                    -- Not: IND ve INF grupları ayrı seçmeli havuzlar (ECTS: 2 adet INF + 1 adet IND + 1 adet CNT seçilir).
                    (66, 'IND471 Yöneylem Araştırması',                           15, 4, TRUE,  'IND-8', 15, 8),  -- T2+U2 (Tamer Özyiğit, prereq ING207) — eski kayıtta öğretmen yanlıştı
                    (67, 'IND472 Mühendislik Ekonomisi',                          15, 4, TRUE,  'IND-8', 15, 8),  -- T2+U2 (Tamer Özyiğit) — eski kayıtta öğretmen yanlıştı
                    (68, 'INF472 Bulut Bilişim',                                    12, 3, TRUE,  'INF-8', 15, 8),  -- T3 (Özgün Pınarer) — eski kayıtta öğretmen yanlıştı
                    (69, 'INF483 Bilgi Çıkarımı ve Veri Madenciliğine Giriş',       3, 3, TRUE,  'INF-8', 15, 8),  -- T3 (Günce Keziban Orman, prereq INF256/INF257) — eski kayıtta yanlışlıkla 'INF437 Modern Ağ Yönetimi' idi (ECTS'de böyle bir ders yok)
                    (70, 'INF473 Üretken Yapay Zekaya Giriş',                      6, 3, TRUE,  'INF-8', 15, 8),  -- T3 (Uzay Çetin) — eski kayıtta öğretmen yanlıştı
                    (71, 'INF474 Wireless and Mobile Networks',                    8, 3, TRUE,  'INF-8', 15, 8),  -- T3 (Ayşegül Tüysüz Erman) — eski kayıtta öğretmen yanlıştı
                    (72, 'INF441 Kriptolojinin Temelleri',                         12, 3, TRUE,  'INF-8', 15, 8),  -- T3 — ECTS Not 19: 'INF441 Şifrelemeye Giriş' yerine geçen güncel ders, kendi coursedetails sayfası yok -- öğretmen ataması doğrulanamadı
                    (73, 'INF475 Kullanıcı Arayüzü ve Deneyimi Tasarımı',        15, 3, TRUE,  'INF-8', 15, 8),  -- T3 (Tamer Özyiğit; ortak: Reis Burak Arslan) — eski kayıtta öğretmen yanlıştı
                    (76, 'CNT416 Sosyal Medya',                                   17, 2, TRUE,  'CNT-8', 15, 8),  -- T2 (Sultan Nezihe Turhan)
                    (77, 'CNT414 Felsefe',                                        33, 2, TRUE,  'CNT-8', 15, 8)  -- T2 (Zübeyde Gaye Çankaya Eksen)
                ON DUPLICATE KEY UPDATE
                    title = VALUES(title), teacher_id = VALUES(teacher_id),
                    weekly_hours = VALUES(weekly_hours), is_elective = VALUES(is_elective),
                    elective_group = VALUES(elective_group),
                    expected_student_count = VALUES(expected_student_count),
                    semester = VALUES(semester);

                -- id=55 ('INF492 Bitirme Projesi I') ve id=62 (INF454'ün 7.yy'daki eski kopyası) ECTS'de
                -- karşılığı olmayan/mükerrer kayıtlardı; DELETE IGNORE ile temizleniyor (canlıda başka
                -- bir tabloya FK ile bağlıysa sessizce atlanır, uygulama başlangıcını bozmaz).
                DELETE IGNORE FROM courses WHERE id IN (55, 62);

                INSERT IGNORE INTO teacher_unavailability (teacher_id, time_slot_id) VALUES
                    (1, 2), (1, 10), (2, 4), (2, 8), (3, 6), (3, 12),
                    (4, 1), (4, 9), (5, 3), (5, 11), (6, 5), (7, 7),
                    (8, 2), (8, 6);

                -- Demo seçmeli kayıtlar (CS-3 → 5. yarıyıl seçmelileri, CS-4 → 7. yarıyıl)
                -- id=56/60, 7.yy'da artık Zorunlu olduğundan (bkz. yukarı) öğrenci 7/8'in eski seçimleri
                -- geçersiz kaldı; Sosyal-7 grubundan (63) geçerli seçimlerle değiştirildi.
                DELETE FROM student_electives
                    WHERE (student_id = 5 AND course_id = 41)
                       OR (student_id = 6 AND course_id = 42)
                       OR (student_id = 7 AND course_id = 56)
                       OR (student_id = 8 AND course_id = 60);
                INSERT IGNORE INTO student_electives (student_id, course_id) VALUES
                    (5, 39),
                    (6, 40),
                    (7, 59), (7, 63),
                    (8, 61), (8, 63);

                -- Demo kayıt talepleri (zaten onaylı)
                INSERT IGNORE INTO enrollments (id, student_id, semester, status) VALUES
                    (1, 1, 1, 'approved'),
                    (2, 2, 1, 'approved'),
                    (3, 3, 3, 'approved'),
                    (4, 4, 3, 'approved'),
                    (5, 5, 5, 'approved'),
                    (6, 6, 5, 'approved'),
                    (7, 7, 7, 'approved'),
                    (8, 8, 7, 'approved');

                -- Eski, yanlış ön koşul kayıtları (id=34 hedefi 4.yy'da INF116 sanılmıştı,
                -- gerçekte 6.yy dersi ve ön koşulu INF256/INF257 OR-grubu — bkz. courses düzeltmesi).
                DELETE FROM course_prerequisites WHERE course_id = 34 AND prerequisite_course_id = 13;

                -- Ön koşul verileri (ECTS'den, 11.07.2026 itibariyle 3-8. yy doğrulandı)
                -- prereq_group: aynı grup numarasındaki kayıtlardan biri yeterli (OR mantığı)
                INSERT IGNORE INTO course_prerequisites (course_id, prerequisite_course_id, prereq_group) VALUES
                    (23, 4,  1),   -- INF224 Veri Yapısı ← INF112 (grup 1)
                    (23, 12, 1),   -- INF224 Veri Yapısı ← INF114 (grup 1, OR)
                    (31, 12, 1),   -- INF243 NOP ← INF114
                    (34, 21, 1),   -- INF334 Bilg. Ağları ← INF256 (grup 1, OR)
                    (34, 29, 1),   -- INF334 Bilg. Ağları ← INF257 (grup 1, OR)
                    (36, 23, 1),   -- INF444 Yapay Zeka ← INF224
                    (56, 34, 1),   -- INF471 Bilişimde Güvenlik ← INF334
                    (38, 30, 1),   -- INF320 Bilg. Mimarisi ← ING220
                    (44, 13, 1),   -- INF333 İşletim Sistemleri ← INF116
                    (49, 35, 1),   -- INF360 Veri Tabanı Yön. ← INF324
                    (53, 32, 1),   -- INF399 Staj II ← INF291
                    (57, 35, 1),   -- INF438 İleri VT ← INF324
                    (58, 12, 1),   -- INF400 Veri Derlemesi ← INF114
                    (60, 12, 1),   -- INF443 Dağıtık ← INF114 (grup 1)
                    (60, 31, 1),   -- INF443 Dağıtık ← INF243 (grup 1, OR)
                    (65, 54, 1),   -- INF494 Bitirme Projesi ← INF493
                    (66, 20, 1),   -- IND471 Yöneylem ← ING207
                    (74, 21, 1),   -- INF356 Veri Analizine Giriş ← INF256 (grup 1, OR)
                    (74, 29, 1),   -- INF356 Veri Analizine Giriş ← INF257 (grup 1, OR)
                    (75, 20, 1),   -- INF325 Sayısal Analiz ← ING207
                    (69, 21, 1),   -- INF483 Bilgi Çıkarımı ← INF256 (grup 1, OR)
                    (69, 29, 1);   -- INF483 Bilgi Çıkarımı ← INF257 (grup 1, OR)
                """);
        }

        private static async Task<bool> RequiresSemesterMigrationAsync(MySqlConnection connection)
        {
            await using var command = new MySqlCommand(
                """
                SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'courses' AND COLUMN_NAME = 'semester';
                """,
                connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 0;
        }

        private static async Task<bool> RequiresBirthDateMigrationAsync(MySqlConnection connection)
        {
            await using var command = new MySqlCommand(
                """
                SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'students' AND COLUMN_NAME = 'birth_date';
                """,
                connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 0;
        }

        private static async Task<bool> RequiresTeacherNumberMigrationAsync(MySqlConnection connection)
        {
            await using var command = new MySqlCommand(
                """
                SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'teachers' AND COLUMN_NAME = 'teacher_number';
                """,
                connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 0;
        }

        private static async Task<bool> RequiresCurrentSemesterMigrationAsync(MySqlConnection connection)
        {
            await using var command = new MySqlCommand(
                """
                SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'students' AND COLUMN_NAME = 'current_semester';
                """,
                connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 0;
        }

        private static async Task<bool> RequiresGroupRemovalMigrationAsync(MySqlConnection connection)
        {
            await using var command = new MySqlCommand(
                """
                SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'courses' AND COLUMN_NAME = 'student_group';
                """,
                connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }

        private static async Task<bool> RequiresScheduleSemesterMigrationAsync(MySqlConnection connection)
        {
            await using var command = new MySqlCommand(
                """
                SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'generated_schedule_sessions' AND COLUMN_NAME = 'semester';
                """,
                connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 0;
        }

        // ── Enrollment methods ─────────────────────────────────────────────────

        public async Task<Enrollment?> GetLatestEnrollmentAsync(int studentId)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                """
                SELECT e.id, e.student_id, e.semester, e.status, e.created_at, e.reviewer_note,
                       s.name AS student_name, s.student_number
                FROM enrollments e
                JOIN students s ON s.id = e.student_id
                WHERE e.student_id = @studentId
                ORDER BY e.id DESC
                LIMIT 1;
                """,
                connection);
            cmd.Parameters.AddWithValue("@studentId", studentId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var enrollment = new Enrollment
            {
                Id = reader.GetInt32("id"),
                StudentId = reader.GetInt32("student_id"),
                Semester = reader.GetInt32("semester"),
                Status = reader.GetString("status"),
                CreatedAt = reader.GetDateTime("created_at"),
                ReviewerNote = reader.GetString("reviewer_note"),
                StudentName = reader.GetString("student_name"),
                StudentNumber = reader.GetString("student_number")
            };
            return enrollment;
        }

        public async Task<List<Enrollment>> GetAllEnrollmentsAsync()
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                """
                SELECT e.id, e.student_id, e.semester, e.status, e.created_at, e.reviewer_note,
                       s.name AS student_name, s.student_number
                FROM enrollments e
                JOIN students s ON s.id = e.student_id
                ORDER BY e.id DESC;
                """,
                connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            var enrollments = new List<Enrollment>();
            while (await reader.ReadAsync())
            {
                enrollments.Add(new Enrollment
                {
                    Id = reader.GetInt32("id"),
                    StudentId = reader.GetInt32("student_id"),
                    Semester = reader.GetInt32("semester"),
                    Status = reader.GetString("status"),
                    CreatedAt = reader.GetDateTime("created_at"),
                    ReviewerNote = reader.GetString("reviewer_note"),
                    StudentName = reader.GetString("student_name"),
                    StudentNumber = reader.GetString("student_number")
                });
            }
            return enrollments;
        }

        // ── Seçmeli ders limitleri ─────────────────────────────────────────────
        // GSÜ Bilgisayar Mühendisliği öğretim programı (ects.gsu.edu.tr) kurallarına göre,
        // yarıyıl + seçmeli grup başına tam olarak kaç ders seçilmesi gerektiği. Bu değerler
        // resmi ders programına sabitlenmiştir (admin panelinden değiştirilemez).
        // Tabloda olmayan bir (yarıyıl, grup) kombinasyonu için varsayılan olarak 1 ders seçilir
        // (bu programdaki neredeyse tüm seçmeli gruplar tam olarak 1 seçim gerektirir).
        private static readonly Dictionary<(int Semester, string Group), int> ElectiveGroupRequiredCounts = new()
        {
            [(3, "Sosyal-3")] = 1,
            [(5, "Teknik-5")] = 1,
            [(6, "Teknik-6")] = 2,
            [(7, "Teknik-7")] = 1,
            [(7, "Sosyal-7")] = 1,
            [(8, "INF-8")] = 2,
            [(8, "IND-8")] = 1,
            [(8, "CNT-8")] = 1,
        };

        private const int DefaultElectiveGroupRequiredCount = 1;

        private static int GetRequiredElectiveCount(int semester, string group) =>
            ElectiveGroupRequiredCounts.TryGetValue((semester, group), out var required)
                ? required
                : DefaultElectiveGroupRequiredCount;

        /// <summary>
        /// Bir yarıyıl için seçilen seçmeli ders id'lerinin, o yarıyılın gerçek seçmeli
        /// gruplarına ve grup başına gereken seçim sayısına uyup uymadığını doğrular.
        /// Sorun yoksa null, varsa kullanıcıya gösterilecek Türkçe hata mesajı döner.
        /// </summary>
        public static string? ValidateElectiveSelection(
            int semester, IReadOnlyList<Course> semesterCourses, IReadOnlyList<int> selectedCourseIds)
        {
            var electivesByGroup = semesterCourses
                .Where(c => c.IsElective)
                .GroupBy(c => c.ElectiveGroup)
                .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToHashSet());

            var validElectiveIds = electivesByGroup.Values.SelectMany(ids => ids).ToHashSet();
            var selectedSet = selectedCourseIds.ToHashSet();

            var unknownIds = selectedSet.Where(id => !validElectiveIds.Contains(id)).ToList();
            if (unknownIds.Count > 0)
                return "Seçilen derslerden biri bu yarıyıla ait geçerli bir seçmeli ders değil.";

            foreach (var (group, courseIds) in electivesByGroup)
            {
                var required = GetRequiredElectiveCount(semester, group);
                var chosen = selectedSet.Count(id => courseIds.Contains(id));
                if (chosen != required)
                {
                    var groupLabel = string.IsNullOrWhiteSpace(group) ? "Seçmeli" : group;
                    return $"'{groupLabel}' grubundan tam olarak {required} ders seçilmelidir (şu an {chosen} seçili).";
                }
            }

            return null;
        }

        /// <summary>
        /// Admin'in dönem sonunda seçtiği öğrencileri bir sonraki yarıyıla ilerletir
        /// (8. yarıyılın üzerine çıkmaz). Yalnızca var olan öğrenci id'leri güncellenir.
        /// </summary>
        public async Task<List<Student>> AdvanceStudentsSemesterAsync(IReadOnlyList<int> studentIds)
        {
            if (studentIds.Count == 0)
                return new List<Student>();

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var placeholders = string.Join(",", studentIds.Select((_, i) => $"@id{i}"));
            await using (var updateCmd = new MySqlCommand(
                $"UPDATE students SET current_semester = LEAST(8, current_semester + 1) WHERE id IN ({placeholders});",
                connection))
            {
                for (var i = 0; i < studentIds.Count; i++)
                    updateCmd.Parameters.AddWithValue($"@id{i}", studentIds[i]);
                await updateCmd.ExecuteNonQueryAsync();
            }

            await using (var selectCmd = new MySqlCommand(
                $"SELECT id, student_number, name, email, birth_date, current_semester FROM students WHERE id IN ({placeholders});",
                connection))
            {
                for (var i = 0; i < studentIds.Count; i++)
                    selectCmd.Parameters.AddWithValue($"@id{i}", studentIds[i]);

                var list = new List<Student>();
                await using var reader = await selectCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var birthDateOrdinal = reader.GetOrdinal("birth_date");
                    list.Add(new Student
                    {
                        Id = reader.GetInt32("id"),
                        StudentNumber = reader.GetString("student_number"),
                        Name = reader.GetString("name"),
                        Email = reader.GetString("email"),
                        BirthDate = reader.IsDBNull(birthDateOrdinal)
                            ? null
                            : DateOnly.FromDateTime(reader.GetDateTime(birthDateOrdinal)),
                        CurrentSemester = reader.GetInt32("current_semester")
                    });
                }
                return list;
            }
        }

        public async Task<Enrollment> CreateEnrollmentAsync(int studentId, int semester, IReadOnlyList<int> electiveCourseIds)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            // Cancel any existing pending enrollment for this student
            await using var cancelCmd = new MySqlCommand(
                "DELETE FROM enrollments WHERE student_id = @sid AND status = 'pending';",
                connection, transaction);
            cancelCmd.Parameters.AddWithValue("@sid", studentId);
            await cancelCmd.ExecuteNonQueryAsync();

            // Insert new enrollment
            await using var insertCmd = new MySqlCommand(
                "INSERT INTO enrollments (student_id, semester, status) VALUES (@sid, @sem, 'pending');",
                connection, transaction);
            insertCmd.Parameters.AddWithValue("@sid", studentId);
            insertCmd.Parameters.AddWithValue("@sem", semester);
            await insertCmd.ExecuteNonQueryAsync();

            var enrollmentId = (int)insertCmd.LastInsertedId;

            // Save selected electives
            await SaveStudentElectivesInTransactionAsync(connection, transaction, studentId, electiveCourseIds);

            await transaction.CommitAsync();

            return (await GetLatestEnrollmentAsync(studentId))!;
        }

        public async Task<Enrollment?> ReviewEnrollmentAsync(int enrollmentId, string status, string reviewerNote)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                """
                UPDATE enrollments
                SET status = @status, reviewer_note = @note
                WHERE id = @id;
                """,
                connection);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@note", reviewerNote);
            cmd.Parameters.AddWithValue("@id", enrollmentId);
            await cmd.ExecuteNonQueryAsync();

            // Return updated enrollment
            await using var getCmd = new MySqlCommand(
                """
                SELECT e.id, e.student_id, e.semester, e.status, e.created_at, e.reviewer_note,
                       s.name AS student_name, s.student_number
                FROM enrollments e
                JOIN students s ON s.id = e.student_id
                WHERE e.id = @id;
                """,
                connection);
            getCmd.Parameters.AddWithValue("@id", enrollmentId);
            await using var reader = await getCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new Enrollment
            {
                Id = reader.GetInt32("id"),
                StudentId = reader.GetInt32("student_id"),
                Semester = reader.GetInt32("semester"),
                Status = reader.GetString("status"),
                CreatedAt = reader.GetDateTime("created_at"),
                ReviewerNote = reader.GetString("reviewer_note"),
                StudentName = reader.GetString("student_name"),
                StudentNumber = reader.GetString("student_number")
            };
        }

        private static async Task SaveStudentElectivesInTransactionAsync(
            MySqlConnection connection,
            MySqlConnector.MySqlTransaction transaction,
            int studentId,
            IReadOnlyList<int> courseIds)
        {
            await using var delCmd = new MySqlCommand(
                "DELETE FROM student_electives WHERE student_id = @sid;",
                connection, transaction);
            delCmd.Parameters.AddWithValue("@sid", studentId);
            await delCmd.ExecuteNonQueryAsync();

            foreach (var courseId in courseIds.Distinct())
            {
                await using var insCmd = new MySqlCommand(
                    "INSERT IGNORE INTO student_electives (student_id, course_id) VALUES (@sid, @cid);",
                    connection, transaction);
                insCmd.Parameters.AddWithValue("@sid", studentId);
                insCmd.Parameters.AddWithValue("@cid", courseId);
                await insCmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<List<Student>> GetAllStudentsAsync()
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT id, student_number, name, email, birth_date, current_semester FROM students ORDER BY name;",
                connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            var list = new List<Student>();
            while (await reader.ReadAsync())
            {
                var birthDateOrdinal = reader.GetOrdinal("birth_date");
                list.Add(new Student
                {
                    Id = reader.GetInt32("id"),
                    StudentNumber = reader.GetString("student_number"),
                    Name = reader.GetString("name"),
                    Email = reader.GetString("email"),
                    BirthDate = reader.IsDBNull(birthDateOrdinal)
                        ? null
                        : DateOnly.FromDateTime(reader.GetDateTime(birthDateOrdinal)),
                    CurrentSemester = reader.GetInt32("current_semester")
                });
            }
            return list;
        }

        public async Task<List<StudentGrade>> GetStudentGradesAsync(int studentId)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                """
                SELECT sg.id, sg.student_id, sg.course_id, sg.grade, sg.passed, sg.created_at,
                       s.name AS student_name, c.title AS course_title, c.semester AS course_semester
                FROM student_grades sg
                JOIN students s ON s.id = sg.student_id
                JOIN courses c ON c.id = sg.course_id
                WHERE sg.student_id = @sid
                ORDER BY c.semester, c.title;
                """,
                connection);
            cmd.Parameters.AddWithValue("@sid", studentId);
            await using var reader = await cmd.ExecuteReaderAsync();
            var list = new List<StudentGrade>();
            while (await reader.ReadAsync())
            {
                list.Add(new StudentGrade
                {
                    Id = reader.GetInt32("id"),
                    StudentId = reader.GetInt32("student_id"),
                    CourseId = reader.GetInt32("course_id"),
                    Grade = reader.GetString("grade"),
                    Passed = reader.GetBoolean("passed"),
                    CreatedAt = reader.GetDateTime("created_at"),
                    StudentName = reader.GetString("student_name"),
                    CourseTitle = reader.GetString("course_title"),
                    CourseSemester = reader.GetInt32("course_semester")
                });
            }
            return list;
        }

        public async Task<StudentGrade> UpsertGradeAsync(int studentId, int courseId, string grade, bool passed)
        {
            // FF her zaman kaldı (passed = false) anlamına gelir; çağıran taraf
            // (admin/öğretmen paneli ya da doğrudan API çağrısı) farklı bir değer
            // gönderse bile burada zorunlu olarak düzeltilir. Böylece transkript
            // ve ön koşul kontrolleri harf notuyla tutarsız kalmaz.
            passed = grade != "FF";

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                """
                INSERT INTO student_grades (student_id, course_id, grade, passed)
                VALUES (@sid, @cid, @grade, @passed)
                ON DUPLICATE KEY UPDATE grade = VALUES(grade), passed = VALUES(passed);
                """,
                connection);
            cmd.Parameters.AddWithValue("@sid", studentId);
            cmd.Parameters.AddWithValue("@cid", courseId);
            cmd.Parameters.AddWithValue("@grade", grade);
            cmd.Parameters.AddWithValue("@passed", passed);
            await cmd.ExecuteNonQueryAsync();

            var grades = await GetStudentGradesAsync(studentId);
            return grades.First(g => g.CourseId == courseId);
        }

        public async Task DeleteGradeAsync(int gradeId)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                "DELETE FROM student_grades WHERE id = @id;",
                connection);
            cmd.Parameters.AddWithValue("@id", gradeId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<Course>> GetCoursesForSemesterAsync(int semester)
        {
            var data = await GetScheduleDataAsync();
            return data.Courses.Where(c => c.Semester == semester).ToList();
        }

        // ── Teacher grade methods ──────────────────────────────────────────────

        /// <summary>
        /// Returns all students enrolled in the teacher's courses (for any semester),
        /// together with each course and whether a grade exists.
        /// </summary>
        public async Task<List<TeacherStudentCourse>> GetStudentsForTeacherAsync(int teacherId)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(
                """
                SELECT
                    s.id           AS student_id,
                    s.name         AS student_name,
                    s.student_number,
                    c.id           AS course_id,
                    c.title        AS course_title,
                    c.semester     AS course_semester,
                    sg.id          AS grade_id,
                    sg.grade,
                    sg.passed
                FROM courses c
                JOIN students s ON
                    -- Ogrenci, bu dersin yariyilina ulasmis veya gecmis olmali
                    -- (onaylanmis en az bir kayit >= ders yariyili) — boylece onceki
                    -- yariyillarin notlari da geriye donuk girilip transkript
                    -- tamamlanabilir.
                    EXISTS (
                        SELECT 1 FROM enrollments e
                        WHERE e.student_id = s.id AND e.status = 'approved' AND e.semester >= c.semester
                    )
                    -- Secmeli derste sadece o dersi gercekten secmis ogrenciler gorunur.
                    AND (
                        c.is_elective = FALSE
                        OR EXISTS (
                            SELECT 1 FROM student_electives se
                            WHERE se.student_id = s.id AND se.course_id = c.id
                        )
                    )
                LEFT JOIN student_grades sg ON sg.student_id = s.id AND sg.course_id = c.id
                WHERE c.teacher_id = @teacherId
                ORDER BY c.semester, c.title, s.name;
                """,
                connection);
            cmd.Parameters.AddWithValue("@teacherId", teacherId);

            var list = new List<TeacherStudentCourse>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var gradeIdOrdinal = reader.GetOrdinal("grade_id");
                var gradeOrdinal   = reader.GetOrdinal("grade");
                var passedOrdinal  = reader.GetOrdinal("passed");
                list.Add(new TeacherStudentCourse
                {
                    StudentId     = reader.GetInt32("student_id"),
                    StudentName   = reader.GetString("student_name"),
                    StudentNumber = reader.GetString("student_number"),
                    CourseId      = reader.GetInt32("course_id"),
                    CourseTitle   = reader.GetString("course_title"),
                    CourseSemester= reader.GetInt32("course_semester"),
                    GradeId       = reader.IsDBNull(gradeIdOrdinal) ? null : reader.GetInt32(gradeIdOrdinal),
                    Grade         = reader.IsDBNull(gradeOrdinal)   ? null : reader.GetString(gradeOrdinal),
                    Passed        = reader.IsDBNull(passedOrdinal)  ? null : reader.GetBoolean(passedOrdinal),
                });
            }
            return list;
        }

        /// <summary>
        /// Checks whether a student satisfies prerequisites for each course in a given semester.
        /// Returns list of unsatisfied prerequisite descriptions.
        /// </summary>
        public async Task<List<PrerequisiteCheckResult>> CheckPrerequisitesAsync(int studentId, int semester)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // Get all courses in the target semester
            await using var courseCmd = new MySqlCommand(
                "SELECT id, title FROM courses WHERE semester = @semester;",
                connection);
            courseCmd.Parameters.AddWithValue("@semester", semester);
            var semesterCourses = new List<(int Id, string Title)>();
            await using (var r = await courseCmd.ExecuteReaderAsync())
                while (await r.ReadAsync())
                    semesterCourses.Add((r.GetInt32("id"), r.GetString("title")));

            // Get the student's passed courses
            await using var gradeCmd = new MySqlCommand(
                "SELECT course_id FROM student_grades WHERE student_id = @sid AND passed = TRUE;",
                connection);
            gradeCmd.Parameters.AddWithValue("@sid", studentId);
            var passedCourseIds = new HashSet<int>();
            await using (var r = await gradeCmd.ExecuteReaderAsync())
                while (await r.ReadAsync())
                    passedCourseIds.Add(r.GetInt32("course_id"));

            // Get prerequisites for each course in this semester
            await using var prereqCmd = new MySqlCommand(
                """
                SELECT cp.course_id, cp.prerequisite_course_id, cp.prereq_group, c.title AS prereq_title
                FROM course_prerequisites cp
                JOIN courses c ON c.id = cp.prerequisite_course_id
                WHERE cp.course_id IN (SELECT id FROM courses WHERE semester = @semester);
                """,
                connection);
            prereqCmd.Parameters.AddWithValue("@semester", semester);

            // Group: course_id → prereq_group → list of (prereq_id, prereq_title)
            var prereqMap = new Dictionary<int, Dictionary<int, List<(int Id, string Title)>>>();
            await using (var r = await prereqCmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var cid    = r.GetInt32("course_id");
                    var pid    = r.GetInt32("prerequisite_course_id");
                    var grp    = r.GetInt32("prereq_group");
                    var ptitle = r.GetString("prereq_title");
                    if (!prereqMap.ContainsKey(cid))  prereqMap[cid]  = new();
                    if (!prereqMap[cid].ContainsKey(grp)) prereqMap[cid][grp] = new();
                    prereqMap[cid][grp].Add((pid, ptitle));
                }
            }

            var results = new List<PrerequisiteCheckResult>();
            foreach (var (courseId, courseTitle) in semesterCourses)
            {
                // Öğrenci bu dersi zaten geçmişse (örn. daha önceki bir kayıt
                // döneminde), ön koşul uyarısı göstermenin bir anlamı yok —
                // ders zaten tamamlanmış durumda.
                if (passedCourseIds.Contains(courseId)) continue;
                if (!prereqMap.ContainsKey(courseId)) continue;
                var unsatisfiedGroups = new List<string>();
                foreach (var (_, groupItems) in prereqMap[courseId])
                {
                    // OR logic: group is satisfied if at least one member is passed
                    bool satisfied = groupItems.Any(p => passedCourseIds.Contains(p.Id));
                    if (!satisfied)
                        unsatisfiedGroups.Add(string.Join(" veya ", groupItems.Select(p => p.Title)));
                }
                if (unsatisfiedGroups.Count > 0)
                {
                    results.Add(new PrerequisiteCheckResult
                    {
                        CourseId    = courseId,
                        CourseTitle = courseTitle,
                        MissingPrerequisites = unsatisfiedGroups
                    });
                }
            }
            return results;
        }

        private static async Task<bool> RequiresHourlyTimeSlotMigrationAsync(MySqlConnection connection)
        {
            await using var command = new MySqlCommand(
                """
                SELECT COUNT(*) AS slot_count,
                       SUM(CASE WHEN hour_range IN ('09:00-11:00', '11:00-13:00', '13:00-15:00') THEN 1 ELSE 0 END) AS legacy_count
                FROM time_slots;
                """,
                connection);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return false;
            }

            var slotCount = reader.GetInt32("slot_count");
            var legacyOrdinal = reader.GetOrdinal("legacy_count");
            var legacyCount = reader.IsDBNull(legacyOrdinal) ? 0 : reader.GetInt32("legacy_count");
            return slotCount > 0 && (slotCount < 30 || legacyCount > 0);
        }

        private static async Task<List<Teacher>> GetTeachersAsync(MySqlConnection connection)
        {
            var teachers = new List<Teacher>();
            await using var command = new MySqlCommand(
                "SELECT id, teacher_number, name, email, password FROM teachers ORDER BY id;",
                connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var teacherNumberOrdinal = reader.GetOrdinal("teacher_number");
                teachers.Add(new Teacher
                {
                    Id = reader.GetInt32("id"),
                    TeacherNumber = reader.IsDBNull(teacherNumberOrdinal) ? string.Empty : reader.GetString(teacherNumberOrdinal),
                    Name = reader.GetString("name"),
                    Email = reader.GetString("email"),
                    Password = string.Empty
                });
            }

            return teachers;
        }

        private static async Task<List<Room>> GetRoomsAsync(MySqlConnection connection)
        {
            var rooms = new List<Room>();
            await using var command = new MySqlCommand(
                "SELECT id, name, capacity, is_amphi FROM rooms ORDER BY id;",
                connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                rooms.Add(new Room
                {
                    Id = reader.GetInt32("id"),
                    Name = reader.GetString("name"),
                    Capacity = reader.GetInt32("capacity"),
                    IsAmphi = reader.GetBoolean("is_amphi")
                });
            }

            return rooms;
        }

        private static async Task<List<TimeSlot>> GetTimeSlotsAsync(MySqlConnection connection)
        {
            var timeSlots = new List<TimeSlot>();
            await using var command = new MySqlCommand(
                "SELECT id, day, hour_range FROM time_slots ORDER BY id;",
                connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                timeSlots.Add(new TimeSlot
                {
                    Id = reader.GetInt32("id"),
                    Day = reader.GetString("day"),
                    HourRange = reader.GetString("hour_range")
                });
            }

            return timeSlots;
        }

        private static async Task<List<Course>> GetCoursesAsync(MySqlConnection connection)
        {
            var courses = new List<Course>();
            await using var command = new MySqlCommand(
                """
                SELECT id, title, teacher_id, weekly_hours, is_elective, elective_group,
                       expected_student_count, semester
                FROM courses
                ORDER BY semester, id;
                """,
                connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                courses.Add(new Course
                {
                    Id = reader.GetInt32("id"),
                    Title = reader.GetString("title"),
                    TeacherId = reader.GetInt32("teacher_id"),
                    WeeklyHours = reader.GetInt32("weekly_hours"),
                    IsElective = reader.GetBoolean("is_elective"),
                    ElectiveGroup = reader.GetString("elective_group"),
                    ExpectedStudentCount = reader.GetInt32("expected_student_count"),
                    Semester = reader.GetInt32("semester")
                });
            }

            return courses;
        }

        private static async Task LoadTeacherUnavailabilityAsync(
            MySqlConnection connection,
            List<Teacher> teachers)
        {
            var teacherById = teachers.ToDictionary(teacher => teacher.Id);
            await using var command = new MySqlCommand(
                "SELECT teacher_id, time_slot_id FROM teacher_unavailability ORDER BY teacher_id, time_slot_id;",
                connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var teacherId = reader.GetInt32("teacher_id");
                if (teacherById.TryGetValue(teacherId, out var teacher))
                {
                    teacher.UnavailabilitySlots.Add(reader.GetInt32("time_slot_id"));
                }
            }
        }

        private static async Task ExecuteAsync(MySqlConnection connection, string sql)
        {
            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
