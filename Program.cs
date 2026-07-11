using System.Collections.Concurrent;
using System.Text.Json;
using GsuTimetablingSystem.Data;
using GsuTimetablingSystem.Models;
using GsuTimetablingSystem.Services;
using MySqlConnector;

const int Port = 5038;

var connectionString = LoadConnectionString();
var adminSettings = LoadAdminSettings();
var adminSessions = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
var teacherSessions = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
var studentSessions = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
var repository = new MySqlScheduleRepository(connectionString);

try
{
    await repository.InitializeAsync();
}
catch (MySqlException exception) when (exception.Number == 1045)
{
    Console.Error.WriteLine("MySQL kullanici adi veya sifresi hatali.");
    Console.Error.WriteLine("Ornek: GSU_MYSQL_PASSWORD='mysql-sifren' dotnet run");
    return;
}
catch (MySqlException exception)
{
    Console.Error.WriteLine($"MySQL baglantisi kurulamadi: {exception.Message}");
    Console.Error.WriteLine("MySQL servisinin acik ve 3306 portunda oldugunu kontrol et.");
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{Port}");

// Keep PascalCase JSON to match the existing JavaScript
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

var app = builder.Build();

// Return JSON for unhandled exceptions instead of plain-text stack traces
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new { Message = ex.Message });
        }
    }
});

app.UseDefaultFiles();   // serves wwwroot/index.html for "/"
app.UseStaticFiles();    // serves wwwroot/gsu.png and everything else in wwwroot/

Console.WriteLine($"GSU Timetabling System: http://127.0.0.1:{Port}/");
Console.WriteLine($"Demo admin: {adminSettings.Email} / {adminSettings.Password}");
Console.WriteLine("Demo student: 2022001 / 19/07/2004");
Console.WriteLine("Demo teacher: 1001 / 1234");

// ── Auth ──────────────────────────────────────────────────────────────────────

app.MapPost("/api/login/student", async (StudentLoginRequest login) =>
{
    var student = await repository.AuthenticateStudentAsync(login.StudentNumber, login.Password);
    if (student is null)
        return Results.Json(new { Message = "Ogrenci bilgileri hatali." }, statusCode: 401);

    var token = Guid.NewGuid().ToString("N");
    studentSessions[token] = student.Id;
    student.Token = token;
    return Results.Ok(student);
});

app.MapPost("/api/login/teacher", async (TeacherLoginRequest login) =>
{
    var teacher = await repository.AuthenticateTeacherAsync(login.TeacherNumber, login.Password);
    if (teacher is null)
        return Results.Json(new { Message = "Ogretmen bilgileri hatali." }, statusCode: 401);

    var token = Guid.NewGuid().ToString("N");
    teacherSessions[token] = teacher.Id;
    teacher.Token = token;
    return Results.Ok(teacher);
});

app.MapPost("/api/login/admin", (AdminLoginRequest login) =>
{
    if (!login.Email.Equals(adminSettings.Email, StringComparison.OrdinalIgnoreCase) ||
        login.Password != adminSettings.Password)
        return Results.Json(new { Message = "Admin bilgileri hatali." }, statusCode: 401);

    var token = Guid.NewGuid().ToString("N");
    adminSessions[token] = adminSettings.Email;
    return Results.Ok(new { adminSettings.Name, adminSettings.Email, Token = token });
});

app.MapPost("/api/logout/admin", (HttpRequest request) =>
{
    if (!TryGetAdminToken(request, adminSessions, out var token))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    adminSessions.TryRemove(token, out _);
    return Results.Ok(new { Message = "Admin cikisi yapildi." });
});

app.MapPost("/api/logout/teacher", (HttpRequest request) =>
{
    if (request.Headers.TryGetValue("X-Teacher-Token", out var value))
        teacherSessions.TryRemove(value.ToString().Trim(), out _);
    return Results.Ok(new { Message = "Cikis yapildi." });
});

app.MapPost("/api/logout/student", (HttpRequest request) =>
{
    if (request.Headers.TryGetValue("X-Student-Token", out var value))
        studentSessions.TryRemove(value.ToString().Trim(), out _);
    return Results.Ok(new { Message = "Cikis yapildi." });
});

// ── Data ──────────────────────────────────────────────────────────────────────

app.MapGet("/api/time-slots", async () =>
{
    var data = await repository.GetScheduleDataAsync();
    return Results.Ok(data.TimeSlots);
});

app.MapGet("/api/rooms", async (HttpRequest request) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    var data = await repository.GetScheduleDataAsync();
    return Results.Ok(data.Rooms);
});

app.MapGet("/api/courses", async (HttpRequest request) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    var data = await repository.GetScheduleDataAsync();
    return Results.Ok(data.Courses);
});

app.MapGet("/api/teachers", async (HttpRequest request) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    return Results.Ok(await repository.GetAllTeachersAsync());
});

app.MapPost("/api/courses", async (HttpRequest request, AddCourseRequest body) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    if (string.IsNullOrWhiteSpace(body.Title))
        return Results.Json(new { Message = "Ders adı boş olamaz." }, statusCode: 400);
    if (body.TeacherId <= 0)
        return Results.Json(new { Message = "Öğretmen seçilmedi." }, statusCode: 400);
    if (body.Semester < 1 || body.Semester > 8)
        return Results.Json(new { Message = "Geçerli bir yarıyıl seçilmeli (1-8)." }, statusCode: 400);

    var course = new GsuTimetablingSystem.Models.Course
    {
        Title = body.Title.Trim(),
        TeacherId = body.TeacherId,
        WeeklyHours = Math.Max(1, body.WeeklyHours),
        IsElective = body.IsElective,
        ElectiveGroup = body.ElectiveGroup.Trim(),
        ExpectedStudentCount = Math.Max(1, body.ExpectedStudentCount),
        Semester = body.Semester
    };

    var created = await repository.AddCourseAsync(course);
    return Results.Ok(created);
});

app.MapDelete("/api/courses/{id:int}", async (HttpRequest request, int id) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    await repository.DeleteCourseAsync(id);
    return Results.Ok(new { Message = "Ders silindi." });
});

// ── Enrollment ────────────────────────────────────────────────────────────────

app.MapGet("/api/courses/semester/{semester:int}", async (int semester) =>
    Results.Ok(await repository.GetCoursesForSemesterAsync(semester)));

app.MapGet("/api/enrollments", async (HttpRequest request) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    return Results.Ok(await repository.GetAllEnrollmentsAsync());
});

app.MapGet("/api/student/enrollment", async (HttpRequest request, int studentId) =>
{
    if (!TryGetStudentId(request, studentSessions, out var authStudentId) || authStudentId != studentId)
        return Results.Json(new { Message = "Öğrenci girişi gereklidir." }, statusCode: 401);

    var enrollment = await repository.GetLatestEnrollmentAsync(studentId);
    return enrollment is null
        ? Results.Json(new { Message = "Kayit bulunamadi." }, statusCode: 404)
        : Results.Ok(enrollment);
});

app.MapPost("/api/student/enrollment", async (HttpRequest request, CreateEnrollmentRequest body) =>
{
    if (!TryGetStudentId(request, studentSessions, out var authStudentId) || authStudentId != body.StudentId)
        return Results.Json(new { Message = "Öğrenci girişi gereklidir." }, statusCode: 401);

    if (body.StudentId <= 0 || body.Semester < 1 || body.Semester > 8)
        return Results.Json(new { Message = "Gecersiz kayit bilgisi." }, statusCode: 400);

    // Öğrenci yalnızca kendi bulunduğu yarıyıl için kayıt talebi oluşturabilir —
    // sınıf atlama/serbest yarıyıl seçimi yok.
    var student = await repository.GetStudentAsync(body.StudentId);
    if (student is null)
        return Results.Json(new { Message = "Ogrenci bulunamadi." }, statusCode: 404);
    if (body.Semester != student.CurrentSemester)
        return Results.Json(new { Message = $"Şu an yalnızca {student.CurrentSemester}. yarıyıl için kayıt talebi oluşturabilirsiniz." }, statusCode: 400);

    // Seçmeli ders sayısı, GSÜ'nün resmi programındaki yarıyıl/grup limitlerine uymalı.
    var semesterCourses = await repository.GetCoursesForSemesterAsync(body.Semester);
    var electiveError = MySqlScheduleRepository.ValidateElectiveSelection(
        body.Semester, semesterCourses, body.ElectiveCourseIds);
    if (electiveError is not null)
        return Results.Json(new { Message = electiveError }, statusCode: 400);

    var enrollment = await repository.CreateEnrollmentAsync(body.StudentId, body.Semester, body.ElectiveCourseIds);
    return Results.Ok(enrollment);
});

app.MapPut("/api/enrollments/{id:int}", async (HttpRequest request, int id, ReviewEnrollmentRequest body) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    if (body.Status != "approved" && body.Status != "rejected")
        return Results.Json(new { Message = "Gecersiz durum." }, statusCode: 400);

    var enrollment = await repository.ReviewEnrollmentAsync(id, body.Status, body.ReviewerNote ?? "");
    return enrollment is null
        ? Results.Json(new { Message = "Kayit bulunamadi." }, statusCode: 404)
        : Results.Ok(enrollment);
});

// ── Student Grades (Admin) ────────────────────────────────────────────────────

app.MapGet("/api/admin/students", async (HttpRequest request) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);
    return Results.Ok(await repository.GetAllStudentsAsync());
});

// POST /api/admin/students/register — yeni öğrenci kaydı: e-posta ve şifre otomatik üretilir
// (e-posta: ad.soyad@ogr.gsu.edu.tr, şifre: doğum tarihi ggAAyyyy formatında).
app.MapPost("/api/admin/students/register", async (HttpRequest request, RegisterStudentRequest body) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    if (string.IsNullOrWhiteSpace(body.Name))
        return Results.Json(new { Message = "Ad soyad boş olamaz." }, statusCode: 400);
    if (string.IsNullOrWhiteSpace(body.StudentNumber))
        return Results.Json(new { Message = "Öğrenci numarası boş olamaz." }, statusCode: 400);
    if (!DateOnly.TryParse(body.BirthDate, out var birthDate))
        return Results.Json(new { Message = "Geçerli bir doğum tarihi girin (YYYY-AA-GG)." }, statusCode: 400);

    try
    {
        var (student, password) = await repository.RegisterStudentAsync(
            body.Name.Trim(), body.StudentNumber.Trim(), birthDate);
        return Results.Ok(new { Student = student, GeneratedPassword = password });
    }
    catch (MySqlException ex) when (ex.Number == 1062)
    {
        return Results.Json(new { Message = "Bu öğrenci numarası zaten kayıtlı." }, statusCode: 409);
    }
});

// POST /api/admin/students/advance-semester — seçilen öğrencileri bir sonraki yarıyıla ilerletir
// (dönem sonunda toplu olarak kullanılır; 8. yarıyılın üzerine çıkmaz).
app.MapPost("/api/admin/students/advance-semester", async (HttpRequest request, AdvanceSemesterRequest body) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    if (body.StudentIds is null || body.StudentIds.Count == 0)
        return Results.Json(new { Message = "En az bir öğrenci seçilmelidir." }, statusCode: 400);

    var updated = await repository.AdvanceStudentsSemesterAsync(body.StudentIds);
    return Results.Ok(updated);
});

app.MapGet("/api/admin/students/{id:int}/grades", async (HttpRequest request, int id) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);
    return Results.Ok(await repository.GetStudentGradesAsync(id));
});

app.MapPost("/api/admin/grades", async (HttpRequest request, UpsertGradeRequest body) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);
    var grade = await repository.UpsertGradeAsync(body.StudentId, body.CourseId, body.Grade, body.Passed);
    return Results.Ok(grade);
});

app.MapDelete("/api/admin/grades/{id:int}", async (HttpRequest request, int id) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);
    await repository.DeleteGradeAsync(id);
    return Results.Ok(new { Message = "Not silindi." });
});

// ── Student ───────────────────────────────────────────────────────────────────

app.MapGet("/api/student/electives", async (HttpRequest request, int studentId) =>
{
    if (!TryGetStudentId(request, studentSessions, out var authStudentId) || authStudentId != studentId)
        return Results.Json(new { Message = "Öğrenci girişi gereklidir." }, statusCode: 401);

    return Results.Ok(await repository.GetElectiveOptionsAsync(studentId));
});

app.MapPost("/api/student/electives", async (HttpRequest request, StudentElectiveRequest selection) =>
{
    if (!TryGetStudentId(request, studentSessions, out var authStudentId) || authStudentId != selection.StudentId)
        return Results.Json(new { Message = "Öğrenci girişi gereklidir." }, statusCode: 401);

    await repository.SaveStudentElectivesAsync(selection.StudentId, selection.CourseIds);
    return Results.Ok(new { Message = "Secmeli dersler kaydedildi." });
});

app.MapGet("/api/student/schedule", async (HttpRequest request, int studentId) =>
{
    if (!TryGetStudentId(request, studentSessions, out var authStudentId) || authStudentId != studentId)
        return Results.Json(new { Message = "Öğrenci girişi gereklidir." }, statusCode: 401);

    var student = await repository.GetStudentAsync(studentId);
    if (student is null)
        return Results.Json(new { Message = "Ogrenci bulunamadi." }, statusCode: 404);

    // Kayit onaylanmadan program gorulemez
    var enrollment = await repository.GetLatestEnrollmentAsync(studentId);
    if (enrollment is null || enrollment.Status != "approved")
        return Results.Json(new { Message = "Kaydiniz henuz onaylanmamistir." }, statusCode: 403);

    var data = await repository.GetScheduleDataAsync();
    var schedule = await repository.GetGeneratedScheduleAsync();
    if (schedule.Count == 0)
        return Results.Json(new { Message = "Program henuz admin tarafindan olusturulmadi." }, statusCode: 400);

    var selectedIds = (await repository.GetStudentElectiveIdsAsync(studentId)).ToHashSet();
    var semesterCourseIds = data.Courses
        .Where(course =>
            course.Semester == enrollment.Semester &&
            (!course.IsElective || selectedIds.Contains(course.Id)))
        .Select(course => course.Id)
        .ToHashSet();

    return Results.Ok(schedule.Where(item => semesterCourseIds.Contains(item.CourseId)));
});

// GET /api/student/grades?studentId=X — öğrencinin kendi transkripti (sadece kendi girişiyle)
app.MapGet("/api/student/grades", async (HttpRequest request, int studentId) =>
{
    if (!TryGetStudentId(request, studentSessions, out var authStudentId) || authStudentId != studentId)
        return Results.Json(new { Message = "Öğrenci girişi gereklidir." }, statusCode: 401);

    return Results.Ok(await repository.GetStudentGradesAsync(studentId));
});

// ── Teacher ───────────────────────────────────────────────────────────────────

app.MapGet("/api/teacher/availability", async (HttpRequest request, int teacherId) =>
{
    if (!TryGetTeacherId(request, teacherSessions, out var authTeacherId) || authTeacherId != teacherId)
        return Results.Json(new { Message = "Öğretmen girişi gereklidir." }, statusCode: 401);

    var data = await repository.GetScheduleDataAsync();
    var teacher = data.Teachers.FirstOrDefault(item => item.Id == teacherId);
    return teacher is null
        ? Results.Json(new { Message = "Ogretmen bulunamadi." }, statusCode: 404)
        : Results.Ok(teacher);
});

app.MapPost("/api/teacher/availability", async (HttpRequest request, TeacherAvailabilityRequest availability) =>
{
    if (!TryGetTeacherId(request, teacherSessions, out var authTeacherId) || authTeacherId != availability.TeacherId)
        return Results.Json(new { Message = "Öğretmen girişi gereklidir." }, statusCode: 401);

    await repository.SaveTeacherUnavailabilityAsync(availability.TeacherId, availability.UnavailableTimeSlotIds);
    return Results.Ok(new { Message = "Musaitlik bilgisi kaydedildi." });
});

app.MapGet("/api/teacher/schedule", async (HttpRequest request, int teacherId) =>
{
    if (!TryGetTeacherId(request, teacherSessions, out var authTeacherId) || authTeacherId != teacherId)
        return Results.Json(new { Message = "Öğretmen girişi gereklidir." }, statusCode: 401);

    var data = await repository.GetScheduleDataAsync();
    var teacherCourseIds = data.Courses
        .Where(course => course.TeacherId == teacherId)
        .Select(course => course.Id)
        .ToHashSet();
    var schedule = await repository.GetGeneratedScheduleAsync();
    if (schedule.Count == 0)
        return Results.Json(new { Message = "Program henuz admin tarafindan olusturulmadi." }, statusCode: 400);

    return Results.Ok(schedule.Where(item => teacherCourseIds.Contains(item.CourseId)));
});

// GET /api/teacher/students?teacherId=X — öğretmenin öğrencileri + not durumları
app.MapGet("/api/teacher/students", async (HttpRequest request, int teacherId) =>
{
    if (!TryGetTeacherId(request, teacherSessions, out var authTeacherId) || authTeacherId != teacherId)
        return Results.Json(new { Message = "Öğretmen girişi gereklidir." }, statusCode: 401);

    var list = await repository.GetStudentsForTeacherAsync(teacherId);
    return Results.Ok(list);
});

// POST /api/teacher/grades — öğretmen not girer (sadece kendi dersleri)
app.MapPost("/api/teacher/grades", async (HttpRequest request, UpsertGradeRequest body) =>
{
    if (!TryGetTeacherId(request, teacherSessions, out var authTeacherId))
        return Results.Json(new { Message = "Öğretmen girişi gereklidir." }, statusCode: 401);

    // Ders gerçekten bu öğretmene mi ait — client'ın gönderdiği hiçbir kimliğe
    // güvenmeden, token'dan çözülen öğretmen id'siyle veritabanından doğrulanır.
    if (!await repository.IsCourseOwnedByTeacherAsync(authTeacherId, body.CourseId))
        return Results.Json(new { Message = "Bu ders size ait değil." }, statusCode: 403);

    var updated = await repository.UpsertGradeAsync(body.StudentId, body.CourseId, body.Grade, body.Passed);
    return Results.Ok(updated);
});

// GET /api/teacher/prerequisites?studentId=X&semester=Y — sadece admin kayıt inceleme ekranında kullanılır
app.MapGet("/api/teacher/prerequisites", async (HttpRequest request, int studentId, int semester) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    var results = await repository.CheckPrerequisitesAsync(studentId, semester);
    return Results.Ok(results);
});

// ── Ön koşul yönetimi (öğretmen kendi dersleri için tanımlar) ─────────────────

// GET /api/teacher/courses?teacherId=X — öğretmenin verdiği dersler
app.MapGet("/api/teacher/courses", async (HttpRequest request, int teacherId) =>
{
    if (!TryGetTeacherId(request, teacherSessions, out var authTeacherId) || authTeacherId != teacherId)
        return Results.Json(new { Message = "Öğretmen girişi gereklidir." }, statusCode: 401);

    return Results.Ok(await repository.GetCoursesByTeacherAsync(teacherId));
});

// GET /api/courses/catalog — ön koşul seçimi için tüm ders kataloğu (herkese açık, hassas veri yok)
app.MapGet("/api/courses/catalog", async () =>
    Results.Ok(await repository.GetAllCoursesBasicAsync()));

// GET /api/teacher/prerequisites/manage?teacherId=X — öğretmenin derslerine tanımlı ön koşullar
app.MapGet("/api/teacher/prerequisites/manage", async (HttpRequest request, int teacherId) =>
{
    if (!TryGetTeacherId(request, teacherSessions, out var authTeacherId) || authTeacherId != teacherId)
        return Results.Json(new { Message = "Öğretmen girişi gereklidir." }, statusCode: 401);

    return Results.Ok(await repository.GetPrerequisitesForTeacherAsync(teacherId));
});

// POST /api/teacher/prerequisites — yeni ön koşul ekle (sadece kendi dersine)
app.MapPost("/api/teacher/prerequisites", async (HttpRequest request, AddPrerequisiteRequest body) =>
{
    if (!TryGetTeacherId(request, teacherSessions, out var authTeacherId) || authTeacherId != body.TeacherId)
        return Results.Json(new { Message = "Öğretmen girişi gereklidir." }, statusCode: 401);

    if (body.CourseId <= 0 || body.PrerequisiteCourseId <= 0)
        return Results.Json(new { Message = "Ders ve ön koşul dersi seçilmelidir." }, statusCode: 400);
    if (body.CourseId == body.PrerequisiteCourseId)
        return Results.Json(new { Message = "Bir ders kendi ön koşulu olamaz." }, statusCode: 400);

    var created = await repository.AddPrerequisiteAsync(
        body.TeacherId, body.CourseId, body.PrerequisiteCourseId, Math.Max(1, body.PrereqGroup));

    return created is null
        ? Results.Json(new { Message = "Bu ders size ait değil." }, statusCode: 403)
        : Results.Ok(created);
});

// DELETE /api/teacher/prerequisites/{id}?teacherId=X
app.MapDelete("/api/teacher/prerequisites/{id:int}", async (HttpRequest request, int id, int teacherId) =>
{
    if (!TryGetTeacherId(request, teacherSessions, out var authTeacherId) || authTeacherId != teacherId)
        return Results.Json(new { Message = "Öğretmen girişi gereklidir." }, statusCode: 401);

    var removed = await repository.DeletePrerequisiteAsync(teacherId, id);
    return removed
        ? Results.Ok(new { Message = "Ön koşul silindi." })
        : Results.Json(new { Message = "Kayıt bulunamadı veya yetkiniz yok." }, statusCode: 404);
});

// ── Admin schedule ────────────────────────────────────────────────────────────

app.MapGet("/api/schedule/admin", async (HttpRequest request) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    return Results.Ok(await repository.GetGeneratedScheduleAsync());
});

app.MapPost("/api/schedule/generate", async (HttpRequest request, GenerateScheduleRequest? body) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    var semester = body?.Semester ?? 0;
    var data = await repository.GetScheduleDataAsync();

    // Güz (odd: 1,3,5,7) ve Bahar (even: 2,4,6,8) dönemleri ayrı haftalık programlardır.
    // Aynı dönem içindeki yarıyıllar sınıf ve öğretmen kaynaklarını paylaşır;
    // farklı dönemler birbirinden bağımsızdır.
    static bool IsFall(int s) => s % 2 == 1;

    if (semester > 0)
    {
        // Tek yarıyıl — yalnızca AYNI DÖNEM içindeki diğer yarıyılları çakışma kümesine al
        var coursesToSchedule = data.Courses.Where(c => c.Semester == semester && c.WeeklyHours > 0).ToList();
        if (!coursesToSchedule.Any())
            return Results.Json(new { Message = "Bu yarıyıl için zamanlanacak ders bulunamadı." }, statusCode: 400);

        var existingSchedule = await repository.GetGeneratedScheduleAsync();
        var sameTermOtherResults = existingSchedule.Where(s =>
        {
            var course = data.Courses.FirstOrDefault(c => c.Id == s.CourseId);
            // Aynı dönem (güz/bahar), farklı yarıyıl
            return course != null && course.Semester != semester && IsFall(course.Semester) == IsFall(semester);
        }).ToList();

        var scheduledData = new ScheduleData
        {
            Teachers = data.Teachers,
            Rooms = data.Rooms,
            TimeSlots = data.TimeSlots,
            Courses = coursesToSchedule
        };

        var (success, newSchedule, errorMessage) = await BuildScheduleAsync(repository, scheduledData, sameTermOtherResults);
        if (!success)
            return Results.Json(new { Message = errorMessage }, statusCode: 400);

        // Bu yarıyılı mevcut kayıtlı programla birleştir (eski yerleşiminin yerine yenisi gelir)
        var allExisting = existingSchedule.Where(s =>
        {
            var course = data.Courses.FirstOrDefault(c => c.Id == s.CourseId);
            return course != null && course.Semester != semester;
        }).ToList();

        var combinedSchedule = allExisting
            .Concat(newSchedule)
            .OrderBy(s => GetDaySortKey(s.Day))
            .ThenBy(s => GetStartMinutes(s.HourRange))
            .ThenBy(s => s.RoomId)
            .ToList();

        await repository.SaveGeneratedScheduleAsync(combinedSchedule);
        return Results.Ok(combinedSchedule);
    }
    else
    {
        // Tüm yarıyıllar: Güz (1→3→5→7) ve Bahar (2→4→6→8) ayrı ayrı oluşturulur.
        // Her dönem kendi içinde sınıf/öğretmen çakışmalarını paylaşır; dönemler arası çakışma yok.
        var fallResults  = new List<ScheduleResult>();
        var springResults = new List<ScheduleResult>();

        foreach (var sem in new[] { 1, 3, 5, 7 })
        {
            var semCourses = data.Courses.Where(c => c.Semester == sem && c.WeeklyHours > 0).ToList();
            if (!semCourses.Any()) continue;
            var semData = new ScheduleData { Teachers = data.Teachers, Rooms = data.Rooms, TimeSlots = data.TimeSlots, Courses = semCourses };
            var (ok, semSched, err) = await BuildScheduleAsync(repository, semData, fallResults);
            if (!ok) return Results.Json(new { Message = $"Güz {sem}. yarıyıl zamanlanamadı: {err}" }, statusCode: 400);
            fallResults.AddRange(semSched);
        }

        foreach (var sem in new[] { 2, 4, 6, 8 })
        {
            var semCourses = data.Courses.Where(c => c.Semester == sem && c.WeeklyHours > 0).ToList();
            if (!semCourses.Any()) continue;
            var semData = new ScheduleData { Teachers = data.Teachers, Rooms = data.Rooms, TimeSlots = data.TimeSlots, Courses = semCourses };
            var (ok, semSched, err) = await BuildScheduleAsync(repository, semData, springResults);
            if (!ok) return Results.Json(new { Message = $"Bahar {sem}. yarıyıl zamanlanamadı: {err}" }, statusCode: 400);
            springResults.AddRange(semSched);
        }

        var allResults = fallResults
            .Concat(springResults)
            .OrderBy(s => GetDaySortKey(s.Day))
            .ThenBy(s => GetStartMinutes(s.HourRange))
            .ThenBy(s => s.RoomId)
            .ToList();

        await repository.SaveGeneratedScheduleAsync(allResults);
        return Results.Ok(allResults);
    }
});

app.MapPost("/api/schedule/adjust", async (HttpRequest request, ManualScheduleAdjustmentRequest adjustment) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    if (adjustment.CourseId <= 0)
        return Results.Json(new { Message = "CourseId gereklidir." }, statusCode: 400);

    if (adjustment.ClearManualAssignment)
    {
        await repository.ClearManualScheduleAssignmentAsync(adjustment.CourseId);
        var currentSched = await repository.GetGeneratedScheduleAsync();
        var remaining = currentSched
            .Where(e => e.CourseId != adjustment.CourseId)
            .OrderBy(e => GetDaySortKey(e.Day))
            .ThenBy(e => GetStartMinutes(e.HourRange))
            .ThenBy(e => e.RoomId)
            .ToList();
        await repository.SaveGeneratedScheduleAsync(remaining);
        return Results.Ok(remaining);
    }

    var sessions = adjustment.Sessions
        .Where(session =>
            session.RoomId is not null &&
            session.StartTimeSlotId is not null &&
            session.DurationHours is not null)
        .Select(session => new ManualScheduleAssignment
        {
            CourseId = adjustment.CourseId,
            RoomId = session.RoomId!.Value,
            StartTimeSlotId = session.StartTimeSlotId!.Value,
            DurationHours = session.DurationHours!.Value
        })
        .ToList();

    if (sessions.Count == 0)
        return Results.Json(new { Message = "En az bir gecerli oturum gereklidir." }, statusCode: 400);

    var existingSchedule = await repository.GetGeneratedScheduleAsync();
    var checkData = await repository.GetScheduleDataAsync();
    var sortedSlots = checkData.TimeSlots
        .OrderBy(s => GetDaySortKey(s.Day))
        .ThenBy(s => GetStartMinutes(s.HourRange))
        .ToList();

    if (existingSchedule.Count > 0)
    {
        var adjustedCourse = checkData.Courses.FirstOrDefault(c => c.Id == adjustment.CourseId);

        foreach (var newSess in sessions)
        {
            var newCovered = GetCoveredSlotIds(sortedSlots, newSess.StartTimeSlotId, newSess.DurationHours);
            if (newCovered.Count == 0) continue;

            foreach (var occupied in existingSchedule.Where(e => e.CourseId != adjustment.CourseId))
            {
                var occupiedCovered = GetCoveredSlotIds(sortedSlots, occupied.TimeSlotId, occupied.DurationHours);
                if (!newCovered.Overlaps(occupiedCovered)) continue;

                if (occupied.RoomId == newSess.RoomId)
                    return Results.Json(new { Message = $"'{occupied.CourseTitle}' dersi bu saatte {occupied.RoomName} odasında. Önce o dersi farklı bir saate taşıyın." }, statusCode: 400);

                if (adjustedCourse is not null && occupied.TeacherId == adjustedCourse.TeacherId)
                    return Results.Json(new { Message = $"{occupied.TeacherName} öğretmeni bu saatte zaten '{occupied.CourseTitle}' dersini veriyor. Önce o dersi farklı bir saate taşıyın." }, statusCode: 400);

                if (adjustedCourse is not null && occupied.Semester == adjustedCourse.Semester)
                    return Results.Json(new { Message = $"{adjustedCourse.Semester}. yarıyıl için bu saatte zaten '{occupied.CourseTitle}' dersi var. Önce o dersi farklı bir saate taşıyın." }, statusCode: 400);
            }
        }
    }

    await repository.SaveManualScheduleAssignmentsAsync(adjustment.CourseId, sessions);

    var manualEntries = BuildManualScheduleResults(sessions, checkData, sortedSlots);
    var updatedEntries = existingSchedule.Where(e => e.CourseId != adjustment.CourseId).ToList();
    updatedEntries.AddRange(manualEntries);
    var updatedSchedule = updatedEntries
        .OrderBy(e => GetDaySortKey(e.Day))
        .ThenBy(e => GetStartMinutes(e.HourRange))
        .ThenBy(e => e.RoomId)
        .ToList();

    await repository.SaveGeneratedScheduleAsync(updatedSchedule);
    return Results.Ok(updatedSchedule);
});

app.MapPost("/api/schedule/reset", async (HttpRequest request) =>
{
    if (!TryGetAdminToken(request, adminSessions, out _))
        return Results.Json(new { Message = "Admin girisi gereklidir." }, statusCode: 401);

    await repository.ClearAllManualAssignmentsAsync();
    await repository.ClearGeneratedScheduleAsync();
    return Results.Ok(new { Message = "Program sifirlandi." });
});

await app.RunAsync();

// ── Helpers ───────────────────────────────────────────────────────────────────

static bool TryGetAdminToken(
    HttpRequest request,
    ConcurrentDictionary<string, string> adminSessions,
    out string token)
{
    token = string.Empty;
    if (!request.Headers.TryGetValue("X-Admin-Token", out var value) ||
        string.IsNullOrWhiteSpace(value))
        return false;

    token = value.ToString().Trim();
    return adminSessions.ContainsKey(token);
}

// Öğretmen/öğrenci oturumları için: header'daki token'ı, giriş sırasında
// üretilmiş olan ilgili teacherId/studentId'ye çözer. Böylece bir istekte
// gönderilen teacherId/studentId artık "güvenilir" değil — sadece token'ın
// sahip olduğu kimlik kabul edilir.
static bool TryGetSessionId(
    HttpRequest request,
    string headerName,
    ConcurrentDictionary<string, int> sessions,
    out int id)
{
    id = 0;
    if (!request.Headers.TryGetValue(headerName, out var value) ||
        string.IsNullOrWhiteSpace(value))
        return false;

    return sessions.TryGetValue(value.ToString().Trim(), out id);
}

static bool TryGetTeacherId(HttpRequest request, ConcurrentDictionary<string, int> teacherSessions, out int teacherId) =>
    TryGetSessionId(request, "X-Teacher-Token", teacherSessions, out teacherId);

static bool TryGetStudentId(HttpRequest request, ConcurrentDictionary<string, int> studentSessions, out int studentId) =>
    TryGetSessionId(request, "X-Student-Token", studentSessions, out studentId);

static async Task<(bool Success, List<ScheduleResult> Schedule, string ErrorMessage)> BuildScheduleAsync(
    MySqlScheduleRepository repository,
    ScheduleData? existingData = null,
    List<ScheduleResult>? existingPlacements = null)
{
    var data = existingData ?? await repository.GetScheduleDataAsync();
    var manualAssignments = await repository.GetManualScheduleAssignmentsAsync();
    var scheduler = new BacktrackingSchedulerService();
    var success = scheduler.TryGenerateSchedule(
        data.Courses,
        data.Teachers,
        data.Rooms,
        data.TimeSlots,
        manualAssignments,
        existingPlacements ?? new List<ScheduleResult>(),
        out var schedule,
        out var errorMessage);

    return success
        ? (true, schedule, string.Empty)
        : (false, new List<ScheduleResult>(), string.IsNullOrWhiteSpace(errorMessage)
            ? "Kisitlara gore cakismasiz program olusturulamadi."
            : errorMessage);
}

static int GetDaySortKey(string day) => day switch
{
    "Pazartesi" => 1,
    "Sali"      => 2,
    "Carsamba"  => 3,
    "Persembe"  => 4,
    "Cuma"      => 5,
    _           => 99
};

static int GetStartMinutes(string hourRange)
{
    var start = hourRange.Split('-', 2)[0].Trim();
    var parts = start.Split(':', 2);
    return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
}

static HashSet<int> GetCoveredSlotIds(IReadOnlyList<TimeSlot> orderedSlots, int startSlotId, int durationHours)
{
    var result = new HashSet<int>();
    var startIndex = -1;
    string? startDay = null;

    for (var i = 0; i < orderedSlots.Count; i++)
    {
        if (orderedSlots[i].Id != startSlotId) continue;
        startIndex = i;
        startDay = orderedSlots[i].Day;
        break;
    }

    if (startIndex < 0) return result;

    for (var offset = 0; offset < durationHours; offset++)
    {
        var index = startIndex + offset;
        if (index >= orderedSlots.Count) break;
        var current = orderedSlots[index];
        if (!current.Day.Equals(startDay, StringComparison.OrdinalIgnoreCase)) break;
        result.Add(current.Id);
    }

    return result;
}

static List<ScheduleResult> BuildManualScheduleResults(
    IReadOnlyList<ManualScheduleAssignment> sessions,
    ScheduleData data,
    IReadOnlyList<TimeSlot> sortedSlots)
{
    var courseById  = data.Courses.ToDictionary(c => c.Id);
    var teacherById = data.Teachers.ToDictionary(t => t.Id);
    var roomById    = data.Rooms.ToDictionary(r => r.Id);
    var slotById    = data.TimeSlots.ToDictionary(s => s.Id);
    var results     = new List<ScheduleResult>();

    foreach (var session in sessions)
    {
        if (!courseById.TryGetValue(session.CourseId, out var course)) continue;
        if (!roomById.TryGetValue(session.RoomId, out var room)) continue;
        if (!slotById.TryGetValue(session.StartTimeSlotId, out var startSlot)) continue;

        teacherById.TryGetValue(course.TeacherId, out var teacher);

        var coveredIds = GetCoveredSlotIds(sortedSlots, session.StartTimeSlotId, session.DurationHours);
        var lastSlot   = sortedSlots.LastOrDefault(s => coveredIds.Contains(s.Id));
        var startTime  = startSlot.HourRange.Split('-', 2)[0].Trim();
        var endTime    = (lastSlot ?? startSlot).HourRange.Split('-', 2)[1].Trim();

        results.Add(new ScheduleResult
        {
            CourseId     = course.Id,
            CourseTitle  = course.Title,
            TeacherId    = course.TeacherId,
            TeacherName  = teacher?.Name ?? "",
            IsElective   = course.IsElective,
            ElectiveGroup = course.ElectiveGroup,
            Semester     = course.Semester,
            RoomId       = room.Id,
            RoomName     = room.Name,
            IsAmphi      = room.IsAmphi,
            TimeSlotId   = session.StartTimeSlotId,
            DurationHours = session.DurationHours,
            Day          = startSlot.Day,
            HourRange    = $"{startTime}-{endTime}",
            IsManual     = true
        });
    }

    return results;
}

static string LoadConnectionString()
{
    var environmentConnectionString = Environment.GetEnvironmentVariable("GSU_MYSQL_CONNECTION_STRING");
    if (!string.IsNullOrWhiteSpace(environmentConnectionString))
        return environmentConnectionString;

    var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    using var document = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
    var configuredConnectionString = document.RootElement
        .GetProperty("ConnectionStrings")
        .GetProperty("DefaultConnection")
        .GetString()
        ?? throw new InvalidOperationException("MySQL connection string bulunamadi.");

    var builder = new MySqlConnectionStringBuilder(configuredConnectionString);
    var user     = Environment.GetEnvironmentVariable("GSU_MYSQL_USER");
    var password = Environment.GetEnvironmentVariable("GSU_MYSQL_PASSWORD");

    if (!string.IsNullOrWhiteSpace(user))     builder.UserID   = user;
    if (password is not null)                 builder.Password = password;

    return builder.ConnectionString;
}

static AdminSettings LoadAdminSettings()
{
    var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    using var document = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
    var adminSection = document.RootElement.GetProperty("Admin");

    var name     = adminSection.GetProperty("Name").GetString()     ?? "Sistem Yonetici";
    var email    = adminSection.GetProperty("Email").GetString()    ?? "admin@gsu.edu.tr";
    var password = adminSection.GetProperty("Password").GetString() ?? "1234";

    var envName     = Environment.GetEnvironmentVariable("GSU_ADMIN_NAME");
    var envEmail    = Environment.GetEnvironmentVariable("GSU_ADMIN_EMAIL");
    var envPassword = Environment.GetEnvironmentVariable("GSU_ADMIN_PASSWORD");

    return new AdminSettings(
        string.IsNullOrWhiteSpace(envName)  ? name  : envName,
        string.IsNullOrWhiteSpace(envEmail) ? email : envEmail,
        envPassword ?? password);
}

internal sealed record AdminSettings(string Name, string Email, string Password);
