using EducationPlatform.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationPlatform.Api.Persistence.Configurations;

public sealed class CourseWeekConfiguration : IEntityTypeConfiguration<CourseWeek>
{
    public void Configure(EntityTypeBuilder<CourseWeek> builder)
    {
        builder.ToTable("CourseWeeks");
        builder.HasKey(week => week.Id);
        builder.Property(week => week.Title).HasMaxLength(200).IsRequired();
        builder.Property(week => week.Order).IsRequired();
        builder.HasIndex("CourseId", nameof(CourseWeek.Order)).IsUnique();
        builder.HasMany(week => week.Contents)
            .WithOne()
            .HasForeignKey("CourseWeekId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(week => week.Contents).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
