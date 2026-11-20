using Microsoft.EntityFrameworkCore;
using EmployeeAssessment.Models;

namespace EmployeeAssessment.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Test> Tests => Set<Test>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Option> Options => Set<Option>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<SubmissionAnswer> SubmissionAnswers => Set<SubmissionAnswer>();
    public DbSet<TaxOffice> TaxOffices => Set<TaxOffice>();
    public DbSet<TaxOfficeSubmission> TaxOfficeSubmissions => Set<TaxOfficeSubmission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Test -> Questions (Cascade delete)
        modelBuilder.Entity<Test>()
            .HasMany(t => t.Questions)
            .WithOne()
            .HasForeignKey(q => q.TestId)
            .OnDelete(DeleteBehavior.Cascade);

        // Question -> Options (Cascade delete)
        modelBuilder.Entity<Question>()
            .HasMany(q => q.Options)
            .WithOne()
            .HasForeignKey(o => o.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Submission -> Answers (Cascade delete)
        modelBuilder.Entity<Submission>()
            .HasMany(s => s.Answers)
            .WithOne()
            .HasForeignKey(a => a.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public static void SeedSampleData(AppDbContext context)
    {
        if (!context.TaxOffices.Any())
        {
            var defaultOffices = new List<TaxOffice>
            {
                new TaxOffice { Name = "مأمورية ضرائب قصر النيل", Code = "TAX-QN", Governorate = "القاهرة" },
                new TaxOffice { Name = "مأمورية ضرائب الدقي", Code = "TAX-DQ", Governorate = "الجيزة" },
                new TaxOffice { Name = "مأمورية ضرائب المعادي", Code = "TAX-MD", Governorate = "القاهرة" },
                new TaxOffice { Name = "مأمورية ضرائب العجوزة", Code = "TAX-AG", Governorate = "الجيزة" },
                new TaxOffice { Name = "مأمورية ضرائب مدينة نصر ثان", Code = "TAX-MN2", Governorate = "القاهرة" },
                new TaxOffice { Name = "مأمورية ضرائب 6 أكتوبر", Code = "TAX-6OCT", Governorate = "الجيزة" },
                new TaxOffice { Name = "مأمورية ضرائب مصر الجديدة", Code = "TAX-HJ", Governorate = "القاهرة" },
                new TaxOffice { Name = "مأمورية ضرائب الإسكندرية أول", Code = "TAX-ALX1", Governorate = "الإسكندرية" },
                new TaxOffice { Name = "مأمورية ضرائب المنصورة أول", Code = "TAX-MAN1", Governorate = "الدقهلية" },
                new TaxOffice { Name = "مأمورية ضرائب طنطا ثان", Code = "TAX-TTA2", Governorate = "الغربية" }
            };
            context.TaxOffices.AddRange(defaultOffices);
            context.SaveChanges();
        }

        if (context.Tests.Any())
        {
            return; // Tests already seeded
        }

        var sampleTest = new Test
        {
            Title = "اختبار الكفاءة المهنية وأمن المعلومات الداخلي",
            Description = "اختبار تقييم عام للموظفين الجدد والحاليين لقياس الوعي ببيئة العمل وأمن المعلومات والعمل الجماعي.",
            DurationMinutes = 15,
            PassingScorePercentage = 70,
            CreatedAt = DateTime.UtcNow,
            Questions = new List<Question>
            {
                new Question
                {
                    Text = "وصلتك رسالة بريد إلكتروني تطلب منك تحديث كلمة المرور فوراً عبر رابط خارجي. ما هو التصرف السليم؟",
                    Type = "mcq",
                    Points = 10,
                    Options = new List<Option>
                    {
                        new Option { Text = "الضغط على الرابط وتحديث كلمة المرور مباشرة لتجنب إيقاف الحساب", IsCorrect = false },
                        new Option { Text = "تجاهل الرسالة وإبلاغ فريق أمن المعلومات / الدعم الفني فوراً", IsCorrect = true },
                        new Option { Text = "إعادة توجيه الرسالة لزملائك في العمل للتأكد منهم", IsCorrect = false },
                        new Option { Text = "الرد على المرسل وطلب تأكيد هويته", IsCorrect = false }
                    }
                },
                new Question
                {
                    Text = "يُسمح للموظف بمشاركة بيانات العملاء الشخصية عبر تطبيقات المراسلة غير الرسمية إذا كان ذلك لتسريع إنجاز العمل.",
                    Type = "true_false",
                    Points = 10,
                    Options = new List<Option>
                    {
                        new Option { Text = "صح", IsCorrect = false },
                        new Option { Text = "خطأ", IsCorrect = true }
                    }
                },
                new Question
                {
                    Text = "ما هو المعيار الأساسي لتحديد أولويات المهام اليومية في بيئة العمل الفعالة؟",
                    Type = "mcq",
                    Points = 10,
                    Options = new List<Option>
                    {
                        new Option { Text = "المهام الأسهل والأسرع أولاً بغض النظر عن الأهمية", IsCorrect = false },
                        new Option { Text = "مصفوفة الأهمية والاستعجال (هام وعاجل)", IsCorrect = true },
                        new Option { Text = "تنفيذ المهام حسب الترتيب الزمني لوصولها فقط", IsCorrect = false },
                        new Option { Text = "تأجيل المهام المعقدة لنهاية الأسبوع دائماً", IsCorrect = false }
                    }
                },
                new Question
                {
                    Text = "قفل شاشة الحاسوب (Lock Screen) عند مغادرة المكتب ولو لفترة قصيرة يُعد من الممارسات الأمنية الإلزامية.",
                    Type = "true_false",
                    Points = 10,
                    Options = new List<Option>
                    {
                        new Option { Text = "صح", IsCorrect = true },
                        new Option { Text = "خطأ", IsCorrect = false }
                    }
                },
                new Question
                {
                    Text = "عند حدوث خلاف في وجهات النظر أثناء اجتماع فريق العمل، ما هو السلوك المهني الأمثل؟",
                    Type = "mcq",
                    Points = 10,
                    Options = new List<Option>
                    {
                        new Option { Text = "الاستماع الفعال، التركيز على مصلحة المشروع، والوصول لحل توافقي مبني على الحقائق", IsCorrect = true },
                        new Option { Text = "الانسحاب من الاجتماع لتجنب النقاش", IsCorrect = false },
                        new Option { Text = "التمسك بالرأي الفردي مهما كانت المعطيات", IsCorrect = false },
                        new Option { Text = "تصعيد الخلاف فوراً للإدارة العليا دون نقاش داخلي", IsCorrect = false }
                    }
                }
            }
        };

        context.Tests.Add(sampleTest);
        context.SaveChanges();
    }
}
