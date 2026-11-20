using System.IO.Compression;
using ClosedXML.Excel;
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

// ==========================================
// 3. Tax Offices & Field Visits Endpoints
// ==========================================

// Get active tax offices for dropdown (Public / Employee)
app.MapGet("/api/tax-offices", async (AppDbContext db) =>
{
    var offices = await db.TaxOffices
        .Where(o => o.IsActive)
        .OrderBy(o => o.Name)
        .Select(o => new
        {
            o.Id,
            o.Name,
            o.Code,
            o.Governorate
        })
        .ToListAsync();

    return Results.Ok(offices);
});

// Submit tax office visit with photo and optional excel sheet (Public / Employee)
app.MapPost("/api/tax-submissions", async (HttpRequest request, AppDbContext db, IWebHostEnvironment env) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { message = "الطلب يجب أن يحتوي على بيانات نموذج (Form Data)" });
    }

    var form = await request.ReadFormAsync();

    if (!int.TryParse(form["taxOfficeId"], out int taxOfficeId) || taxOfficeId <= 0)
    {
        return Results.BadRequest(new { message = "يرجى اختيار مأمورية الضرائب" });
    }

    string employeeName = form["employeeName"].ToString().Trim();
    string employeeCode = form["employeeCode"].ToString().Trim();
    string notes = form["notes"].ToString().Trim();
    bool hasExcel = form["hasExcelSheet"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

    if (string.IsNullOrWhiteSpace(employeeName) || string.IsNullOrWhiteSpace(employeeCode))
    {
        return Results.BadRequest(new { message = "اسم الموظف والرقم الوظيفي مطلوبان" });
    }

    var office = await db.TaxOffices.FindAsync(taxOfficeId);
    if (office == null)
    {
        return Results.BadRequest(new { message = "المأمورية المحددة غير موجودة" });
    }

    var visitPhoto = form.Files.GetFile("visitPhoto");
    if (visitPhoto == null || visitPhoto.Length == 0)
    {
        return Results.BadRequest(new { message = "صورة نموذج الزيارة إلزامية" });
    }

    // Determine Cairo / local date string yyyy-MM-dd
    string todayStr = DateTime.UtcNow.AddHours(2).ToString("yyyy-MM-dd");
    var webRoot = env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");

    var visitsDir = Path.Combine(webRoot, "uploads", "visits", todayStr);
    Directory.CreateDirectory(visitsDir);

    var photoExt = Path.GetExtension(visitPhoto.FileName);
    if (string.IsNullOrEmpty(photoExt)) photoExt = ".jpg";
    var photoFileName = $"{Guid.NewGuid():N}{photoExt}";
    var photoFullPath = Path.Combine(visitsDir, photoFileName);

    using (var photoStream = new FileStream(photoFullPath, FileMode.Create))
    {
        await visitPhoto.CopyToAsync(photoStream);
    }
    string visitPhotoUrl = $"/uploads/visits/{todayStr}/{photoFileName}";

    string? excelSheetUrl = null;
    string? originalExcelName = null;
    int rowCount = 0;

    var excelFile = form.Files.GetFile("excelSheet");
    if (hasExcel && excelFile != null && excelFile.Length > 0)
    {
        var sheetsDir = Path.Combine(webRoot, "uploads", "sheets", todayStr);
        Directory.CreateDirectory(sheetsDir);

        originalExcelName = Path.GetFileName(excelFile.FileName);
        var excelExt = Path.GetExtension(excelFile.FileName);
        if (string.IsNullOrEmpty(excelExt)) excelExt = ".xlsx";

        var excelFileName = $"{Guid.NewGuid():N}{excelExt}";
        var excelFullPath = Path.Combine(sheetsDir, excelFileName);

        using (var sheetStream = new FileStream(excelFullPath, FileMode.Create))
        {
            await excelFile.CopyToAsync(sheetStream);
        }
        excelSheetUrl = $"/uploads/sheets/{todayStr}/{excelFileName}";

        try
        {
            using var workbook = new XLWorkbook(excelFullPath);
            var sheet = workbook.Worksheets.FirstOrDefault();
            if (sheet != null)
            {
                rowCount = Math.Max(0, sheet.RowsUsed().Count() - 1);
            }
        }
        catch { rowCount = 0; }
    }

    var submission = new TaxOfficeSubmission
    {
        TaxOfficeId = office.Id,
        TaxOfficeName = office.Name,
        EmployeeName = employeeName,
        EmployeeCode = employeeCode,
        SubmittedDate = todayStr,
        SubmittedAt = DateTime.UtcNow,
        VisitPhotoUrl = visitPhotoUrl,
        HasExcelSheet = hasExcel && !string.IsNullOrEmpty(excelSheetUrl),
        ExcelSheetUrl = excelSheetUrl,
        OriginalExcelFileName = originalExcelName,
        RowCount = rowCount,
        Notes = notes
    };

    db.TaxOfficeSubmissions.Add(submission);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        success = true,
        message = "تم تسليم بيانات المأمورية ونموذج الزيارة بنجاح",
        id = submission.Id,
        taxOfficeName = office.Name,
        submittedAt = submission.SubmittedAt
    });
});

// Admin: Get all Tax Offices (for management)
app.MapGet("/api/admin/tax-offices", async (AppDbContext db) =>
{
    var offices = await db.TaxOffices
        .OrderBy(o => o.Name)
        .ToListAsync();

    return Results.Ok(offices);
});

// Admin: Add new custom Tax Office
app.MapPost("/api/admin/tax-offices", async (CreateTaxOfficeDto dto, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(dto.Name))
    {
        return Results.BadRequest(new { message = "اسم مأمورية الضرائب مطلوب" });
    }

    var office = new TaxOffice
    {
        Name = dto.Name.Trim(),
        Code = dto.Code?.Trim(),
        Governorate = dto.Governorate?.Trim(),
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    };

    db.TaxOffices.Add(office);
    await db.SaveChangesAsync();

    return Results.Created($"/api/admin/tax-offices/{office.Id}", office);
});

// Admin: Update / Toggle Tax Office
app.MapPut("/api/admin/tax-offices/{id:int}", async (int id, UpdateTaxOfficeDto dto, AppDbContext db) =>
{
    var office = await db.TaxOffices.FindAsync(id);
    if (office == null)
    {
        return Results.NotFound(new { message = "المأمورية غير موجودة" });
    }

    if (!string.IsNullOrWhiteSpace(dto.Name))
    {
        office.Name = dto.Name.Trim();
    }
    office.Code = dto.Code?.Trim();
    office.Governorate = dto.Governorate?.Trim();
    office.IsActive = dto.IsActive;

    await db.SaveChangesAsync();
    return Results.Ok(office);
});

// Admin: Delete Tax Office
app.MapDelete("/api/admin/tax-offices/{id:int}", async (int id, AppDbContext db) =>
{
    var office = await db.TaxOffices.FindAsync(id);
    if (office == null)
    {
        return Results.NotFound(new { message = "المأمورية غير موجودة" });
    }

    db.TaxOffices.Remove(office);
    await db.SaveChangesAsync();
    return Results.Ok(new { success = true });
});

// Admin: Get Tax Submissions by Date (with summary & missing offices)
app.MapGet("/api/admin/tax-submissions", async (string? date, AppDbContext db) =>
{
    string targetDate = string.IsNullOrWhiteSpace(date)
        ? DateTime.UtcNow.AddHours(2).ToString("yyyy-MM-dd")
        : date.Trim();

    var submissions = await db.TaxOfficeSubmissions
        .Where(s => s.SubmittedDate == targetDate)
        .OrderByDescending(s => s.SubmittedAt)
        .ToListAsync();

    var allOffices = await db.TaxOffices
        .Where(o => o.IsActive)
        .OrderBy(o => o.Name)
        .ToListAsync();

    var submittedOfficeIds = submissions.Select(s => s.TaxOfficeId).Distinct().ToHashSet();
    var missingOffices = allOffices.Where(o => !submittedOfficeIds.Contains(o.Id)).ToList();

    int totalActiveOffices = allOffices.Count;
    int submittedCount = submittedOfficeIds.Count;
    int sheetsCount = submissions.Count(s => s.HasExcelSheet);
    int totalRowsExtracted = submissions.Sum(s => s.RowCount);

    return Results.Ok(new
    {
        date = targetDate,
        totalActiveOffices,
        submittedCount,
        missingCount = missingOffices.Count,
        sheetsCount,
        totalRowsExtracted,
        submissions,
        missingOffices
    });
});

// Admin: Delete a Tax Submission
app.MapDelete("/api/admin/tax-submissions/{id:int}", async (int id, AppDbContext db, IWebHostEnvironment env) =>
{
    var submission = await db.TaxOfficeSubmissions.FindAsync(id);
    if (submission == null)
    {
        return Results.NotFound(new { message = "السجل غير موجود" });
    }

    var webRoot = env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");

    // Attempt to delete photo file
    try
    {
        if (!string.IsNullOrEmpty(submission.VisitPhotoUrl))
        {
            var pPath = Path.Combine(webRoot, submission.VisitPhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(pPath)) File.Delete(pPath);
        }
        if (!string.IsNullOrEmpty(submission.ExcelSheetUrl))
        {
            var sPath = Path.Combine(webRoot, submission.ExcelSheetUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(sPath)) File.Delete(sPath);
        }
    }
    catch { /* Ignore file deletion errors */ }

    db.TaxOfficeSubmissions.Remove(submission);
    await db.SaveChangesAsync();

    return Results.Ok(new { success = true });
});

// Admin: Export Master Excel Sheet Merging All Submissions for Date
app.MapGet("/api/admin/tax-submissions/export-excel", async (string? date, AppDbContext db, IWebHostEnvironment env) =>
{
    string targetDate = string.IsNullOrWhiteSpace(date)
        ? DateTime.UtcNow.AddHours(2).ToString("yyyy-MM-dd")
        : date.Trim();

    var submissions = await db.TaxOfficeSubmissions
        .Where(s => s.SubmittedDate == targetDate && s.HasExcelSheet && s.ExcelSheetUrl != null)
        .OrderBy(s => s.TaxOfficeName)
        .ToListAsync();

    var webRoot = env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");

    using var masterWorkbook = new XLWorkbook();
    var masterSheet = masterWorkbook.Worksheets.Add("بيانات الممولين المجمعة");
    masterSheet.RightToLeft = true;

    int masterRow = 1;
    bool headerInitialized = false;

    foreach (var sub in submissions)
    {
        var localPath = Path.Combine(webRoot, sub.ExcelSheetUrl!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(localPath)) continue;

        try
        {
            using var subWb = new XLWorkbook(localPath);
            var subSheet = subWb.Worksheets.FirstOrDefault();
            if (subSheet == null) continue;

            var rows = subSheet.RowsUsed().ToList();
            if (rows.Count == 0) continue;

            // Initialize Header Row once from the first valid excel sheet
            if (!headerInitialized)
            {
                masterSheet.Cell(1, 1).Value = "المأمورية";
                masterSheet.Cell(1, 2).Value = "اسم الموظف";
                masterSheet.Cell(1, 3).Value = "الرقم الوظيفي";
                masterSheet.Cell(1, 4).Value = "وقت التسليم";

                var headerRow = rows[0];
                int colOffset = 5;
                foreach (var cell in headerRow.CellsUsed())
                {
                    masterSheet.Cell(1, colOffset++).Value = cell.Value;
                }

                int totalCols = Math.Max(4, colOffset - 1);
                var hRange = masterSheet.Range(1, 1, 1, totalCols);
                hRange.Style.Font.Bold = true;
                hRange.Style.Fill.BackgroundColor = XLColor.FromArgb(30, 58, 138); // Navy blue
                hRange.Style.Font.FontColor = XLColor.White;
                hRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                headerInitialized = true;
                masterRow = 2;
            }

            // Append data rows (skipping original header at index 0)
            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                masterSheet.Cell(masterRow, 1).Value = sub.TaxOfficeName;
                masterSheet.Cell(masterRow, 2).Value = sub.EmployeeName;
                masterSheet.Cell(masterRow, 3).Value = sub.EmployeeCode;
                masterSheet.Cell(masterRow, 4).Value = sub.SubmittedAt.AddHours(2).ToString("hh:mm tt");

                int colOffset = 5;
                foreach (var cell in row.CellsUsed())
                {
                    masterSheet.Cell(masterRow, colOffset++).Value = cell.Value;
                }

                masterRow++;
            }
        }
        catch { /* Skip corrupted files */ }
    }

    if (!headerInitialized)
    {
        masterSheet.Cell(1, 1).Value = $"لا توجد شيتات إكسيل مرفوعة لتاريخ اليوم ({targetDate})";
        masterSheet.Cell(1, 1).Style.Font.Bold = true;
    }
    else
    {
        masterSheet.Columns().AdjustToContents();
    }

    using var ms = new MemoryStream();
    masterWorkbook.SaveAs(ms);
    var bytes = ms.ToArray();

    return Results.File(
        bytes,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"Master_Taxpayers_{targetDate}.xlsx"
    );
});

// Admin: Export ZIP archive of all visit form photos for Date
app.MapGet("/api/admin/tax-submissions/export-photos", async (string? date, AppDbContext db, IWebHostEnvironment env) =>
{
    string targetDate = string.IsNullOrWhiteSpace(date)
        ? DateTime.UtcNow.AddHours(2).ToString("yyyy-MM-dd")
        : date.Trim();

    var submissions = await db.TaxOfficeSubmissions
        .Where(s => s.SubmittedDate == targetDate && !string.IsNullOrEmpty(s.VisitPhotoUrl))
        .OrderBy(s => s.TaxOfficeName)
        .ToListAsync();

    var webRoot = env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");

    using var ms = new MemoryStream();
    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
    {
        int index = 1;
        foreach (var sub in submissions)
        {
            var localPath = Path.Combine(webRoot, sub.VisitPhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(localPath)) continue;

            var ext = Path.GetExtension(localPath);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";

            string safeOffice = string.Concat(sub.TaxOfficeName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            string safeEmp = string.Concat(sub.EmployeeName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            string entryName = $"{index:D2}_{safeOffice}_{safeEmp}{ext}";

            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var fileStream = File.OpenRead(localPath);
            fileStream.CopyTo(entryStream);

            index++;
        }
    }
    var bytes = ms.ToArray();

    return Results.File(
        bytes,
        "application/zip",
        $"Visit_Forms_{targetDate}.zip"
    );
});

app.Run();
