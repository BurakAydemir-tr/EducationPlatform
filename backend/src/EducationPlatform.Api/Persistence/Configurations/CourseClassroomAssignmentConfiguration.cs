using EducationPlatform.Api.Persistence.Courses;
using EducationPlatform.Domain.Classrooms;
using EducationPlatform.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationPlatform.Api.Persistence.Configurations;

public sealed class CourseClassroomAssignmentConfiguration : IEntityTypeConfiguration<CourseClassroomAssignment>
{
    public void Configure(EntityTypeBuilder<CourseClassroomAssignment> builder)
    {
        builder.ToTable("CourseClassroomAssignments");
        builder.HasKey(assignment => assignment.Id);
        builder.HasIndex(assignment => new { assignment.CourseId, assignment.ClassroomId })
            .HasDatabaseName(CourseClassroomAssignment.ActiveAssignmentIndexName)
            .IsUnique()
            .HasFilter("\"RemovedAt\" IS NULL");
        builder.HasOne<Course>().WithMany().HasForeignKey(assignment => assignment.CourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Classroom>().WithMany().HasForeignKey(assignment => assignment.ClassroomId).OnDelete(DeleteBehavior.Restrict);
    }
}
