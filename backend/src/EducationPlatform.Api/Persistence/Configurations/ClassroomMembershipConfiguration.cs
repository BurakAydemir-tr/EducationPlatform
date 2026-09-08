using EducationPlatform.Api.Persistence.Classrooms;
using EducationPlatform.Domain.Classrooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationPlatform.Api.Persistence.Configurations;

internal sealed class ClassroomMembershipConfiguration : IEntityTypeConfiguration<ClassroomMembership>
{
    public void Configure(EntityTypeBuilder<ClassroomMembership> builder)
    {
        builder.ToTable("ClassroomMemberships");
        builder.HasKey(membership => membership.Id);
        builder.Property(membership => membership.Id).ValueGeneratedNever();

        builder.HasIndex(membership => new { membership.ClassroomId, membership.StudentId })
            .IsUnique()
            .HasDatabaseName(ClassroomMembership.ActiveMembershipIndexName)
            .HasFilter("\"LeftAt\" IS NULL");

        builder.HasOne<Classroom>()
            .WithMany()
            .HasForeignKey(membership => membership.ClassroomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Identity.ApplicationUser>()
            .WithMany()
            .HasForeignKey(membership => membership.StudentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
