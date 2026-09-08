using EducationPlatform.Api.Persistence.Identity;
using EducationPlatform.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationPlatform.Api.Persistence.Configurations;

public sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("Courses");
        builder.HasKey(course => course.Id);
        builder.Property(course => course.Title).HasMaxLength(200).IsRequired();
        builder.Property(course => course.Description).HasMaxLength(2000);
        builder.Property(course => course.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(course => course.TeacherId);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(course => course.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(course => course.Weeks)
            .WithOne()
            .HasForeignKey("CourseId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(course => course.Weeks).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
