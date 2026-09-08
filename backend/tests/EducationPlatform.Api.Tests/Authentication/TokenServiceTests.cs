using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Persistence.Identity;
using Microsoft.Extensions.Options;

namespace EducationPlatform.Api.Tests.Authentication;

public sealed class TokenServiceTests
{
    [Fact]
    public void CreateAccessToken_ContainsOnlyRequiredIdentityClaims()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Name = "Teacher Name",
            UserName = "teacher"
        };
        var service = CreateService();

        var value = service.CreateAccessToken(user, [RoleNames.Teacher]);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(value);

        Assert.Contains(token.Claims, claim => claim.Type == ClaimTypes.NameIdentifier && claim.Value == user.Id.ToString());
        Assert.Contains(token.Claims, claim => claim.Type == ClaimTypes.Role && claim.Value == RoleNames.Teacher);
        Assert.DoesNotContain(token.Claims, claim => claim.Value == user.Name);
        Assert.DoesNotContain(token.Claims, claim => claim.Value == user.UserName);
    }

    [Fact]
    public void CreateRefreshToken_ReturnsOnlyHashForPersistence()
    {
        var service = CreateService();

        var token = service.CreateRefreshToken();

        Assert.NotEqual(token.Value, token.Hash);
        Assert.Equal(TokenService.HashRefreshToken(token.Value), token.Hash);
        Assert.Equal(64, token.Hash.Length);
    }

    private static TokenService CreateService() => new(
        Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = "test-signing-key-that-is-at-least-32-bytes-long"
        }),
        TimeProvider.System);
}
