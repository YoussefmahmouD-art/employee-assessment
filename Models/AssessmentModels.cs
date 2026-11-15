namespace EmployeeAssessment.Models;

public class Test
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; } = 15;
    public int PassingScorePercentage { get; set; } = 60;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Question> Questions { get; set; } = new();
    public List<Submission> Submissions { get; set; } = new();
}

public class Question
{
    public int Id { get; set; }
    public int TestId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Type { get; set; } = "mcq"; // "mcq" or "true_false"
    public int Points { get; set; } = 1;

    public List<Option> Options { get; set; } = new();
}

public class Option
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; } = false;
}

public class Submission
{
    public int Id { get; set; }
    public int TestId { get; set; }
    public string TestTitle { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string? Department { get; set; }
    public int TotalScore { get; set; }
    public int MaxScore { get; set; }
    public double Percentage { get; set; }
    public bool IsPassed { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public List<SubmissionAnswer> Answers { get; set; } = new();
}

public class SubmissionAnswer
{
    public int Id { get; set; }
    public int SubmissionId { get; set; }
    public int QuestionId { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public int? SelectedOptionId { get; set; }
    public string? SelectedOptionText { get; set; }
    public string? CorrectOptionText { get; set; }
    public bool IsCorrect { get; set; }
    public int PointsEarned { get; set; }
}

// DTOs
public class TakeTestDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; }
    public int TotalQuestions { get; set; }
    public List<TakeQuestionDto> Questions { get; set; } = new();
}

public class TakeQuestionDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Type { get; set; } = "mcq";
    public int Points { get; set; }
    public List<TakeOptionDto> Options { get; set; } = new();
}

public class TakeOptionDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    // Note: IsCorrect is intentionally omitted so employee cannot inspect it!
}

public class SubmitAnswerItem
{
    public int QuestionId { get; set; }
    public int? SelectedOptionId { get; set; }
}

public class SubmitTestRequest
{
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string? Department { get; set; }
    public List<SubmitAnswerItem> Answers { get; set; } = new();
}

public class CreateOptionDto
{
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}

public class CreateQuestionDto
{
    public string Text { get; set; } = string.Empty;
    public string Type { get; set; } = "mcq";
    public int Points { get; set; } = 1;
    public List<CreateOptionDto> Options { get; set; } = new();
}

public class CreateTestRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; } = 15;
    public int PassingScorePercentage { get; set; } = 60;
    public List<CreateQuestionDto> Questions { get; set; } = new();
}

public class AdminLoginRequest
{
    public string Password { get; set; } = string.Empty;
}
