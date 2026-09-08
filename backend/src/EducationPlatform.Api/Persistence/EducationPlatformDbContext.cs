using EducationPlatform.Api.Persistence.Classrooms;
using EducationPlatform.Api.Persistence.Identity;
using EducationPlatform.Domain.Classrooms;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EducationPlatform.Api.Persistence;

public sealed class EducationPlatformDbContext(
    DbContextOptions<EducationPlatformDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Classroom> Classrooms => Set<Classroom>();

    public DbSet<ClassroomMembership> ClassroomMemberships => Set<ClassroomMembership>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(EducationPlatformDbContext).Assembly);
    }
}
