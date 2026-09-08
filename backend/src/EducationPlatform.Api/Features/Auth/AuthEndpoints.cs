using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Common.Results;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Api.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EducationPlatform.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");
        group.MapPost("/login", LoginAsync);
        group.MapPost("/refresh", RefreshAsync);
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        EducationPlatformDbContext dbContext,
        TokenService tokenService)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result<LoginResponse>.Failure(new Error(
                "invalid_credentials",
                "The username or password is invalid.",
                ErrorType.Authentication)).ToHttpResult(httpContext);
        }

        var user = await userManager.FindByNameAsync(request.UserName.Trim());
        if (user is null)
        {
            return Result<LoginResponse>.Failure(new Error(
                "invalid_credentials",
                "The username or password is invalid.",
                ErrorType.Authentication)).ToHttpResult(httpContext);
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);
        if (!signInResult.Succeeded)
        {
            return Result<LoginResponse>.Failure(new Error(
                "invalid_credentials",
                "The username or password is invalid.",
                ErrorType.Authentication)).ToHttpResult(httpContext);
        }

        var roles = await userManager.GetRolesAsync(user);
        var refreshToken = tokenService.CreateRefreshToken();
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshToken.Hash,
            ExpiresAt = refreshToken.ExpiresAt
        });
        await dbContext.SaveChangesAsync(httpContext.RequestAborted);

        return Result<LoginResponse>.Success(new LoginResponse(
            tokenService.CreateAccessToken(user, roles),
            refreshToken.Value,
            tokenService.GetAccessTokenExpiration())).ToHttpResult(httpContext);
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        EducationPlatformDbContext dbContext,
        TokenService tokenService,
        TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return InvalidRefreshToken(httpContext);
        }

        var hash = TokenService.HashRefreshToken(request.RefreshToken);
        var storedToken = await dbContext.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == hash, httpContext.RequestAborted);

        var now = timeProvider.GetUtcNow();
        if (storedToken is null || storedToken.RevokedAt is not null || storedToken.ExpiresAt <= now)
        {
            return InvalidRefreshToken(httpContext);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(httpContext.RequestAborted);
        var revoked = await dbContext.RefreshTokens
            .Where(token => token.Id == storedToken.Id
                && token.RevokedAt == null
                && token.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, now),
                httpContext.RequestAborted);
        if (revoked == 0)
        {
            await transaction.RollbackAsync(httpContext.RequestAborted);
            return InvalidRefreshToken(httpContext);
        }

        var replacement = tokenService.CreateRefreshToken();
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = storedToken.UserId,
            TokenHash = replacement.Hash,
            ExpiresAt = replacement.ExpiresAt
        });
        await dbContext.SaveChangesAsync(httpContext.RequestAborted);
        await transaction.CommitAsync(httpContext.RequestAborted);

        var roles = await userManager.GetRolesAsync(storedToken.User);
        return Result<LoginResponse>.Success(new LoginResponse(
            tokenService.CreateAccessToken(storedToken.User, roles),
            replacement.Value,
            tokenService.GetAccessTokenExpiration())).ToHttpResult(httpContext);
    }

    private static IResult InvalidRefreshToken(HttpContext httpContext) =>
        Result<LoginResponse>.Failure(new Error(
            "invalid_refresh_token",
            "The refresh token is invalid or expired.",
            ErrorType.Authentication)).ToHttpResult(httpContext);
}

public sealed record LoginRequest(string UserName, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LoginResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);
