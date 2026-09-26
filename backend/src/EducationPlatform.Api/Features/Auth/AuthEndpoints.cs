using System.ComponentModel.DataAnnotations;
using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Common.Results;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Api.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EducationPlatform.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");
        group.MapPost("/login", LoginAsync);
        group.MapPost("/refresh", RefreshAsync);
        group.MapPost("/teacher-registration", RegisterTeacherAsync);
        return endpoints;
    }

    private static async Task<IResult> RegisterTeacherAsync(
        TeacherRegistrationRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        EducationPlatformDbContext db)
    {
        var validation = ValidateRegistration(request);
        if (validation is not null) return RegistrationValidation(context, validation);

        var email = request.Email.Trim();
        var normalized = userManager.NormalizeName(email);
        if (await db.Users.AnyAsync(user => user.NormalizedUserName == normalized
            || user.NormalizedEmail == normalized
            || (user.StudentCode != null && user.StudentCode.ToUpper() == normalized), context.RequestAborted))
            return RegistrationConflict(context);

        await using var transaction = await db.Database.BeginTransactionAsync(context.RequestAborted);
        var teacher = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email,
            Name = request.Name.Trim(), Surname = request.Surname.Trim(),
            TeacherAccountStatus = TeacherAccountStatus.Pending
        };

        IdentityResult created;
        try
        {
            created = await userManager.CreateAsync(teacher, request.Password);
        }
        catch (DbUpdateException exception) when (IsRegistrationUniquenessViolation(exception))
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return RegistrationConflict(context);
        }
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            if (created.Errors.Any(error => error.Code is nameof(IdentityErrorDescriber.DuplicateUserName)
                or nameof(IdentityErrorDescriber.DuplicateEmail))) return RegistrationConflict(context);
            return RegistrationValidation(context, string.Join(" ", created.Errors.Select(error => error.Description)));
        }

        try
        {
            var roleResult = await userManager.AddToRoleAsync(teacher, RoleNames.Teacher);
            if (!roleResult.Succeeded) throw new InvalidOperationException("The Teacher role is not configured correctly.");
            await transaction.CommitAsync(context.RequestAborted);
        }
        catch (DbUpdateException exception) when (IsRegistrationUniquenessViolation(exception))
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return RegistrationConflict(context);
        }

        return Results.Created($"/api/admin/teachers/{teacher.Id}",
            new TeacherRegistrationResponse(teacher.Id, teacher.Email!, teacher.Name, teacher.Surname!, teacher.TeacherAccountStatus.Value));
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
        if (request.UserName.Trim().Length > 256)
        {
            return Result<LoginResponse>.Failure(new Error(
                "invalid_credentials", "The username or password is invalid.", ErrorType.Authentication)).ToHttpResult(httpContext);
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
        var statusFailure = TeacherStatusFailure(user, roles, httpContext);
        if (statusFailure is not null) return statusFailure;
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
        if (request.RefreshToken.Length > 2048) return InvalidRefreshToken(httpContext);

        var hash = TokenService.HashRefreshToken(request.RefreshToken);
        var storedToken = await dbContext.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == hash, httpContext.RequestAborted);

        var now = timeProvider.GetUtcNow();
        if (storedToken is null || storedToken.RevokedAt is not null || storedToken.ExpiresAt <= now)
        {
            return InvalidRefreshToken(httpContext);
        }

        var roles = await userManager.GetRolesAsync(storedToken.User);
        var statusFailure = TeacherStatusFailure(storedToken.User, roles, httpContext);
        if (statusFailure is not null)
        {
            await dbContext.RefreshTokens.Where(token => token.UserId == storedToken.UserId && token.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, now), httpContext.RequestAborted);
            return statusFailure;
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

    private static IResult? TeacherStatusFailure(ApplicationUser user, IEnumerable<string> roles, HttpContext context)
    {
        if (!roles.Contains(RoleNames.Teacher)) return null;
        return user.TeacherAccountStatus switch
        {
            TeacherAccountStatus.Active => null,
            TeacherAccountStatus.Pending => StatusFailure(context, "teacher_approval_pending", "Teacher account is awaiting approval."),
            TeacherAccountStatus.Rejected => StatusFailure(context, "teacher_account_rejected", "Teacher account registration was rejected."),
            TeacherAccountStatus.Disabled => StatusFailure(context, "teacher_account_disabled", "Teacher account is disabled."),
            _ => StatusFailure(context, "teacher_account_disabled", "Teacher account is not active.")
        };
    }

    private static IResult StatusFailure(HttpContext context, string code, string description) =>
        Result.Failure(new Error(code, description, ErrorType.Authorization)).ToHttpResult(context);

    private static string? ValidateRegistration(TeacherRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Trim().Length > 256
            || !new EmailAddressAttribute().IsValid(request.Email.Trim())) return "A valid email address of at most 256 characters is required.";
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            return "Name is required and cannot exceed 200 characters.";
        if (string.IsNullOrWhiteSpace(request.Surname) || request.Surname.Trim().Length > 200)
            return "Surname is required and cannot exceed 200 characters.";
        if (string.IsNullOrWhiteSpace(request.Password)) return "Password is required.";
        return null;
    }

    private static bool IsRegistrationUniquenessViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres
        && postgres.SqlState == PostgresErrorCodes.UniqueViolation
        && postgres.ConstraintName is ApplicationUser.UserNameIndexName
            or ApplicationUser.StudentCodeIndexName
            or ApplicationUser.IdentifierNamespaceConstraintName
            or ApplicationUser.EmailIndexName;

    private static IResult RegistrationValidation(HttpContext context, string detail) =>
        Result.Failure(new Error("invalid_teacher_registration", detail, ErrorType.Validation)).ToHttpResult(context);

    private static IResult RegistrationConflict(HttpContext context) =>
        Result.Failure(new Error("teacher_account_exists", "An account with the same email or identifier already exists.", ErrorType.Conflict)).ToHttpResult(context);
}

public sealed record LoginRequest(string UserName, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LoginResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

public sealed record TeacherRegistrationRequest(string Email, string Name, string Surname, string Password);

public sealed record TeacherRegistrationResponse(Guid UserId, string Email, string Name, string Surname, TeacherAccountStatus AccountStatus);
