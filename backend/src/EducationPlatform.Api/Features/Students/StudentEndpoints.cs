using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Common.Results;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Api.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EducationPlatform.Api.Features.Students;

public static class StudentEndpoints
{
    public static IEndpointRouteBuilder MapStudentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/students", CreateAsync)
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Teacher));
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateStudentRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        EducationPlatformDbContext dbContext)
    {
        if (string.IsNullOrWhiteSpace(request.UserName)
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.InitialPassword))
        {
            return ValidationFailure(httpContext, "Username, name and initial password are required.");
        }

        var userName = request.UserName.Trim();
        var name = request.Name.Trim();
        var studentCode = string.IsNullOrWhiteSpace(request.StudentCode)
            ? null
            : request.StudentCode.Trim();
        var normalizedUserName = userManager.NormalizeName(userName);
        var normalizedStudentCode = studentCode is null
            ? null
            : userManager.NormalizeName(studentCode);

        if (studentCode is not null && studentCode.Length > 64)
        {
            return ValidationFailure(httpContext, "Student code cannot exceed 64 characters.");
        }

        if (normalizedUserName == normalizedStudentCode
            || await dbContext.Users.AnyAsync(
                user => user.NormalizedUserName == normalizedUserName
                    || (user.StudentCode != null && user.StudentCode.ToUpper() == normalizedUserName)
                    || (normalizedStudentCode != null
                        && (user.NormalizedUserName == normalizedStudentCode
                            || (user.StudentCode != null
                                && user.StudentCode.ToUpper() == normalizedStudentCode))),
                httpContext.RequestAborted))
        {
            return DuplicateAccount(httpContext);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(httpContext.RequestAborted);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Name = name,
            StudentCode = studentCode
        };

        IdentityResult createResult;
        try
        {
            createResult = await userManager.CreateAsync(user, request.InitialPassword);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException postgresException
            && IsIdentifierUniquenessViolation(postgresException))
        {
            await transaction.RollbackAsync(httpContext.RequestAborted);
            return DuplicateAccount(httpContext);
        }

        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(httpContext.RequestAborted);
            if (createResult.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.DuplicateUserName)))
            {
                return DuplicateAccount(httpContext);
            }

            return ValidationFailure(
                httpContext,
                string.Join(" ", createResult.Errors.Select(error => error.Description)));
        }

        var roleResult = await userManager.AddToRoleAsync(user, RoleNames.Student);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException("The Student role is not configured correctly.");
        }

        await transaction.CommitAsync(httpContext.RequestAborted);
        var response = new CreateStudentResponse(user.Id, user.UserName!, user.Name, user.StudentCode);
        return Result<CreateStudentResponse>.Success(response)
            .ToCreatedHttpResult(httpContext, $"/api/students/{user.Id}");
    }

    private static IResult ValidationFailure(HttpContext httpContext, string description) =>
        Result<CreateStudentResponse>.Failure(new Error(
            "invalid_student_account",
            description,
            ErrorType.Validation)).ToHttpResult(httpContext);

    private static IResult DuplicateAccount(HttpContext httpContext) =>
        Result<CreateStudentResponse>.Failure(new Error(
            "duplicate_student_account",
            "A user with the same username or student code already exists.",
            ErrorType.Conflict)).ToHttpResult(httpContext);

    private static bool IsIdentifierUniquenessViolation(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UniqueViolation
        && exception.ConstraintName is ApplicationUser.StudentCodeIndexName
            or ApplicationUser.UserNameIndexName
            or ApplicationUser.IdentifierNamespaceConstraintName;
}

public sealed record CreateStudentRequest(
    string UserName,
    string Name,
    string InitialPassword,
    string? StudentCode);

public sealed record CreateStudentResponse(Guid Id, string UserName, string Name, string? StudentCode);
