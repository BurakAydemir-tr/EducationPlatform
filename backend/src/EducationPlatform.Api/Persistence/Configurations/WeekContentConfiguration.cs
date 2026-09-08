using EducationPlatform.Domain.Courses;
using EducationPlatform.Domain.Quizzes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationPlatform.Api.Persistence.Configurations;

public sealed class WeekContentConfiguration : IEntityTypeConfiguration<WeekContent>
{
    public const string QuizContentIndexName = "IX_WeekContents_QuizId";

    public void Configure(EntityTypeBuilder<WeekContent> builder)
    {
        builder.ToTable("WeekContents");
        builder.HasKey(content => content.Id);
        builder.Property(content => content.Title).HasMaxLength(200).IsRequired();
        builder.Property(content => content.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(content => content.TopicText).HasMaxLength(20000);
        builder.Property(content => content.VideoUrl).HasMaxLength(2000);
        builder.Property(content => content.VideoDescription).HasMaxLength(2000);
        builder.Property(content => content.IsActive).IsRequired();
        builder.HasIndex("CourseWeekId", nameof(WeekContent.Order))
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
        builder.HasIndex(content => content.QuizId)
            .HasDatabaseName(QuizContentIndexName)
            .IsUnique()
            .HasFilter("\"QuizId\" IS NOT NULL");
        builder.HasOne<Quiz>()
            .WithOne()
            .HasForeignKey<WeekContent>(content => content.QuizId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
