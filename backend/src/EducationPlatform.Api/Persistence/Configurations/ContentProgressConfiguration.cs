using EducationPlatform.Api.Persistence.Identity;
using EducationPlatform.Domain.Courses;
using EducationPlatform.Domain.Progress;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationPlatform.Api.Persistence.Configurations;

public sealed class ContentProgressConfiguration : IEntityTypeConfiguration<ContentProgress>
{
    public const string PrimaryKeyName = "PK_ContentProgress";

    public void Configure(EntityTypeBuilder<ContentProgress> builder)
    {
        builder.ToTable("ContentProgress");
        builder.HasKey(progress => new { progress.StudentId, progress.ContentId }).HasName(PrimaryKeyName);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(progress => progress.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WeekContent>().WithMany().HasForeignKey(progress => progress.ContentId).OnDelete(DeleteBehavior.Restrict);
    }
}
