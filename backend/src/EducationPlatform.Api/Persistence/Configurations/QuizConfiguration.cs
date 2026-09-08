using EducationPlatform.Api.Persistence.Identity;
using EducationPlatform.Domain.Quizzes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationPlatform.Api.Persistence.Configurations;

public sealed class QuizConfiguration : IEntityTypeConfiguration<Quiz>
{
    public void Configure(EntityTypeBuilder<Quiz> builder)
    {
        builder.ToTable("Quizzes");
        builder.HasKey(quiz => quiz.Id);
        builder.Property(quiz => quiz.Title).HasMaxLength(200).IsRequired();
        builder.HasIndex(quiz => quiz.TeacherId);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(quiz => quiz.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(quiz => quiz.Questions).WithOne().HasForeignKey("QuizId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(quiz => quiz.Questions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.ToTable("Questions");
        builder.HasKey(question => question.Id);
        builder.Property(question => question.Text).HasMaxLength(4000).IsRequired();
        builder.HasIndex("QuizId", nameof(Question.Order)).IsUnique();
        builder.HasMany(question => question.Options).WithOne().HasForeignKey("QuestionId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(question => question.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class OptionConfiguration : IEntityTypeConfiguration<Option>
{
    public void Configure(EntityTypeBuilder<Option> builder)
    {
        builder.ToTable("Options");
        builder.HasKey(option => option.Id);
        builder.Property(option => option.Text).HasMaxLength(2000).IsRequired();
        builder.HasIndex("QuestionId", nameof(Option.Order)).IsUnique();
    }
}
