using EducationPlatform.Domain.Classrooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationPlatform.Api.Persistence.Configurations;

internal sealed class ClassroomConfiguration : IEntityTypeConfiguration<Classroom>
{
    public void Configure(EntityTypeBuilder<Classroom> builder)
    {
        builder.ToTable("Classrooms");
        builder.HasKey(classroom => classroom.Id);
        builder.Property(classroom => classroom.Id).ValueGeneratedNever();
        builder.Property(classroom => classroom.Name).IsRequired();
        builder.Property(classroom => classroom.TeacherId).IsRequired();

        builder.HasOne<Identity.ApplicationUser>()
            .WithMany()
            .HasForeignKey(classroom => classroom.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
