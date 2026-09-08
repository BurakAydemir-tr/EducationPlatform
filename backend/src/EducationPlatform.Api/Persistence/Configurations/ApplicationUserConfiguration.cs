using EducationPlatform.Api.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationPlatform.Api.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.Name).IsRequired();
        builder.Property(user => user.StudentCode).HasMaxLength(64);
        builder.HasIndex(user => user.StudentCode)
            .IsUnique()
            .HasDatabaseName(ApplicationUser.StudentCodeIndexName);
    }
}
