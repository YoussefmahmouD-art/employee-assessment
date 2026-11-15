using Microsoft.EntityFrameworkCore;
using EmployeeAssessment.Data;
using EmployeeAssessment.Models;

var builder = WebApplication.CreateBuilder(args);

// Configure port for local development while allowing IIS / Cloud to manage ports automatically
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APP_POOL_ID")) &&
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_PORT")))
{
    builder.WebHost.UseUrls("http://0.0.0.0:5000");
}

// Configure SQLite Database with absolute path to prevent working directory issues on IIS / Cloud
var dbPath = Path.Combine(AppContext.BaseDirectory, "assessment.db");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

// Admin Secret Key (Can be modified here or via environment)
const string ADMIN_PASSWORD = "admin@2026";

// Admin Security Middleware for /api/admin/* routes
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase))
    {
        // Allow verify-login without key check
        if (path.Equals("/api/admin/verify-login", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var providedKey = context.Request.Headers["X-Admin-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedKey) || providedKey != ADMIN_PASSWORD)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "غير مصرح بالدخول، يرجى إدخال كلمة مرور الإدارة" });
            return;
        }
    }

    await next();
});

// Ensure DB is created and seeded on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    AppDbContext.SeedSampleData(db);
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// ==========================================
// 1. Employee Endpoints
// ==========================================

// Get list of available tests for employees
app.MapGet("/api/tests", async (AppDbContext db) =>
{
    var tests = await db.Tests
        .Select(t => new
        {
            t.Id,
            t.Title,
            t.Description,
            t.DurationMinutes,
            t.PassingScorePercentage,
            TotalQuestions = t.Questions.Count
        })
        .ToListAsync();

    return Results.Ok(tests);
});

// Get test details for taking exam (SECURITY: IsCorrect is NOT sent to client!)
app.MapGet("/api/tests/{id:int}", async (int id, AppDbContext db) =>
{
    var test = await db.Tests
        .Include(t => t.Questions)
        .ThenInclude(q => q.Options)
        .FirstOrDefaultAsync(t => t.Id == id);

    if (test == null)
    {
        return Results.NotFound(new { message = "الاختبار غير موجود" });
    }

    var dto = new TakeTestDto
    {
        Id = test.Id,
        Title = test.Title,
        Description = test.Description,
        DurationMinutes = test.DurationMinutes,
        TotalQuestions = test.Questions.Count,
        Questions = test.Questions.Select(q => new TakeQuestionDto
        {
            Id = q.Id,
            Text = q.Text,
            Type = q.Type,
            Points = q.Points,
            Options = q.Options.Select(o => new TakeOptionDto
            {
                Id = o.Id,
                Text = o.Text
            }).ToList()
        }).ToList()
    };

    return Results.Ok(dto);
});

// Submit test answers by employee
app.MapPost("/api/tests/{id:int}/submit", async (int id, SubmitTestRequest request, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.EmployeeName) || string.IsNullOrWhiteSpace(request.EmployeeCode))
    {
        return Results.BadRequest(new { message = "اسم الموظف والرقم الوظيفي مطلوبان" });
    }

    var test = await db.Tests
        .Include(t => t.Questions)
        .ThenInclude(q => q.Options)
        .FirstOrDefaultAsync(t => t.Id == id);

    if (test == null)
    {
        return Results.NotFound(new { message = "الاختبار غير موجود" });
    }

    int totalEarnedScore = 0;
    int maxPossibleScore = test.Questions.Sum(q => q.Points);
    if (maxPossibleScore == 0) maxPossibleScore = 1; // prevent divide-by-zero

    var submissionAnswers = new List<SubmissionAnswer>();

    foreach (var question in test.Questions)
    {
        var submittedAnswer = request.Answers.FirstOrDefault(a => a.QuestionId == question.Id);
        int? selectedOptionId = submittedAnswer?.SelectedOptionId;

        var selectedOption = question.Options.FirstOrDefault(o => o.Id == selectedOptionId);
        var correctOption = question.Options.FirstOrDefault(o => o.IsCorrect);

        bool isCorrect = selectedOption != null && selectedOption.IsCorrect;
        int pointsEarned = isCorrect ? question.Points : 0;
        totalEarnedScore += pointsEarned;

        submissionAnswers.Add(new SubmissionAnswer
        {
            QuestionId = question.Id,
            QuestionText = question.Text,
            SelectedOptionId = selectedOptionId,
            SelectedOptionText = selectedOption?.Text ?? "لم تتم الإجابة",
            CorrectOptionText = correctOption?.Text ?? "غير محدد",
            IsCorrect = isCorrect,
            PointsEarned = pointsEarned
        });
    }

    double percentage = Math.Round(((double)totalEarnedScore / maxPossibleScore) * 100, 1);
    bool isPassed = percentage >= test.PassingScorePercentage;

    var submission = new Submission
    {
        TestId = test.Id,
        TestTitle = test.Title,
        EmployeeName = request.EmployeeName.Trim(),
        EmployeeCode = request.EmployeeCode.Trim(),
        Department = string.IsNullOrWhiteSpace(request.Department) ? "عام" : request.Department.Trim(),
        TotalScore = totalEarnedScore,
        MaxScore = maxPossibleScore,
        Percentage = percentage,
        IsPassed = isPassed,
        SubmittedAt = DateTime.UtcNow,
        Answers = submissionAnswers
    };

    db.Submissions.Add(submission);
    await db.SaveChangesAsync();

    // STRICT PRIVACY: Return only confirmation, NO score, NO answers
    return Results.Ok(new
    {
        success = true,
        message = "تم تسليم الاختبار بنجاح. تم حفظ إجاباتك وإرسالها للإدارة للتقييم.",
        employeeName = submission.EmployeeName,
        submittedAt = submission.SubmittedAt
    });
});

// ==========================================
// 2. Admin Endpoints
// ==========================================

// Verify Admin Password
app.MapPost("/api/admin/verify-login", (AdminLoginRequest req) =>
{
    if (req.Password == ADMIN_PASSWORD)
    {
        return Results.Ok(new { success = true, token = ADMIN_PASSWORD });
    }
    return Results.Unauthorized();
});

// Create new test with questions & options
app.MapPost("/api/admin/tests", async (CreateTestRequest request, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Title))
    {
        return Results.BadRequest(new { message = "عنوان الاختبار مطلوب" });
    }

    if (request.Questions == null || request.Questions.Count == 0)
    {
        return Results.BadRequest(new { message = "يجب إضافة سؤال واحد على الأقل" });
    }

    var newTest = new Test
    {
        Title = request.Title.Trim(),
        Description = request.Description?.Trim(),
        DurationMinutes = request.DurationMinutes > 0 ? request.DurationMinutes : 15,
        PassingScorePercentage = request.PassingScorePercentage > 0 ? request.PassingScorePercentage : 60,
        CreatedAt = DateTime.UtcNow,
        Questions = request.Questions.Select(q => new Question
        {
            Text = q.Text.Trim(),
            Type = q.Type ?? "mcq",
            Points = q.Points > 0 ? q.Points : 1,
            Options = q.Options.Select(o => new Option
            {
                Text = o.Text.Trim(),
                IsCorrect = o.IsCorrect
            }).ToList()
        }).ToList()
    };

    db.Tests.Add(newTest);
    await db.SaveChangesAsync();

    return Results.Created($"/api/tests/{newTest.Id}", new { success = true, id = newTest.Id });
});

// Delete a test
app.MapDelete("/api/admin/tests/{id:int}", async (int id, AppDbContext db) =>
{
    var test = await db.Tests.FindAsync(id);
    if (test == null)
    {
        return Results.NotFound(new { message = "الاختبار غير موجود" });
    }

    db.Tests.Remove(test);
    await db.SaveChangesAsync();
    return Results.Ok(new { success = true });
});

// Get all submissions for Admin Dashboard
app.MapGet("/api/admin/submissions", async (AppDbContext db) =>
{
    var submissions = await db.Submissions
        .OrderByDescending(s => s.SubmittedAt)
        .Select(s => new
        {
            s.Id,
            s.TestId,
            s.TestTitle,
            s.EmployeeName,
            s.EmployeeCode,
            s.Department,
            s.TotalScore,
            s.MaxScore,
            s.Percentage,
            s.IsPassed,
            s.SubmittedAt
        })
        .ToListAsync();

    return Results.Ok(submissions);
});

// Get single submission detailed evaluation
app.MapGet("/api/admin/submissions/{id:int}", async (int id, AppDbContext db) =>
{
    var submission = await db.Submissions
        .Include(s => s.Answers)
        .FirstOrDefaultAsync(s => s.Id == id);

    if (submission == null)
    {
        return Results.NotFound(new { message = "النتيجة غير موجودة" });
    }

    return Results.Ok(submission);
});

// Delete a submission
app.MapDelete("/api/admin/submissions/{id:int}", async (int id, AppDbContext db) =>
{
    var submission = await db.Submissions.FindAsync(id);
    if (submission == null)
    {
        return Results.NotFound(new { message = "النتيجة غير موجودة" });
    }

    db.Submissions.Remove(submission);
    await db.SaveChangesAsync();
    return Results.Ok(new { success = true });
});

// Admin stats summary
app.MapGet("/api/admin/stats", async (AppDbContext db) =>
{
    var totalTests = await db.Tests.CountAsync();
    var totalSubmissions = await db.Submissions.CountAsync();
    var passedCount = await db.Submissions.CountAsync(s => s.IsPassed);
    var failedCount = totalSubmissions - passedCount;

    var avgPercentage = totalSubmissions > 0
        ? Math.Round(await db.Submissions.AverageAsync(s => s.Percentage), 1)
        : 0;

    return Results.Ok(new
    {
        totalTests,
        totalSubmissions,
        passedCount,
        failedCount,
        passRate = totalSubmissions > 0 ? Math.Round(((double)passedCount / totalSubmissions) * 100, 1) : 0,
        averagePercentage = avgPercentage
    });
});

app.Run();
