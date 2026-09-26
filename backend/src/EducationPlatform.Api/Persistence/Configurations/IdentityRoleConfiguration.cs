using EducationPlatform.Api.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationPlatform.Api.Persistence.Configurations;

internal sealed class IdentityRoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public static readonly Guid TeacherRoleId = Guid.Parse("8f044a1d-46e9-4b06-a810-779b50844d42");
    public static readonly Guid StudentRoleId = Guid.Parse("e1011df9-3755-49fb-88b0-bcc38850bccc");
    public static readonly Guid AdminRoleId = Guid.Parse("6482b681-4a15-4e88-a822-fad3e70c4091");

    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.HasData(
            CreateRole(TeacherRoleId, RoleNames.Teacher),
            CreateRole(StudentRoleId, RoleNames.Student),
            CreateRole(AdminRoleId, RoleNames.Admin));
    }

    private static IdentityRole<Guid> CreateRole(Guid id, string name) => new(name)
    {
        Id = id,
        NormalizedName = name.ToUpperInvariant(),
        ConcurrencyStamp = id.ToString()
    };
}
