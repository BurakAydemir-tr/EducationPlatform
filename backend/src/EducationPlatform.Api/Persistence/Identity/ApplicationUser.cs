using Microsoft.AspNetCore.Identity;

namespace EducationPlatform.Api.Persistence.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public const string StudentCodeIndexName = "UX_AspNetUsers_StudentCode";
    public const string UserNameIndexName = "UserNameIndex";
    public const string IdentifierNamespaceConstraintName = "PK_UserIdentifiers";

    public required string Name { get; set; }

    public string? StudentCode { get; set; }
}
