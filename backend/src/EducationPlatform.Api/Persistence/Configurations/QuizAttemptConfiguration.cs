using EducationPlatform.Api.Persistence.Identity;
using EducationPlatform.Domain.Quizzes;
using EducationPlatform.Domain.QuizAttempts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationPlatform.Api.Persistence.Configurations;

public sealed class QuizAttemptConfiguration : IEntityTypeConfiguration<QuizAttempt>
{
    public void Configure(EntityTypeBuilder<QuizAttempt> builder)
    {
        builder.ToTable("QuizAttempts");
        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Score).HasColumnType("numeric");
        builder.Ignore(attempt => attempt.IsCompleted);
        builder.HasIndex(attempt => new { attempt.StudentId, attempt.QuizId, attempt.CompletedAt });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(attempt => attempt.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Quiz>().WithMany().HasForeignKey(attempt => attempt.QuizId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(attempt => attempt.Answers).WithOne().HasForeignKey("QuizAttemptId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(attempt => attempt.Answers).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class QuizAttemptAnswerConfiguration : IEntityTypeConfiguration<QuizAttemptAnswer>
{
    public void Configure(EntityTypeBuilder<QuizAttemptAnswer> builder)
    {
        builder.ToTable("QuizAttemptAnswers");
        builder.HasKey(answer => answer.Id);
        builder.HasIndex("QuizAttemptId", nameof(QuizAttemptAnswer.QuestionId)).IsUnique();
    }
}
