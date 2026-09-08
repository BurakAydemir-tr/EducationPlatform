using System.Security.Claims;

namespace EducationPlatform.Api.Authentication;

public static class CurrentUser
{
    public static bool TryGetId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
