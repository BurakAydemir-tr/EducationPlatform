using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Common.Results;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Api.Persistence.Configurations;
using EducationPlatform.Api.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EducationPlatform.Api.Features.Admin;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/teachers")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));
        group.MapGet("/", ListAsync);
        group.MapGet("/pending", ListPendingAsync);
        group.MapGet("/{teacherId:guid}", GetAsync);
        group.MapPost("/{teacherId:guid}/approve", ApproveAsync);
        group.MapPost("/{teacherId:guid}/reject", RejectAsync);
        group.MapPost("/{teacherId:guid}/disable", DisableAsync);
        group.MapPost("/{teacherId:guid}/reset-password", ResetPasswordAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(HttpContext context, EducationPlatformDbContext db) =>
        Results.Ok(await TeacherQuery(db)
            .OrderBy(user => user.Email)
            .Select(user => ToResponse(user))
            .ToListAsync(context.RequestAborted));

    private static async Task<IResult> ListPendingAsync(HttpContext context, EducationPlatformDbContext db) =>
        Results.Ok(await TeacherQuery(db)
            .Where(user => user.TeacherAccountStatus == TeacherAccountStatus.Pending)
            .OrderBy(user => user.Email)
            .Select(user => new TeacherAdminResponse(
                user.Id,
                user.Email!,
                user.Name,
                user.Surname,
                user.TeacherAccountStatus!.Value,
                user.EmailConfirmed))
            .ToListAsync(context.RequestAborted));

    private static IQueryable<ApplicationUser> TeacherQuery(EducationPlatformDbContext db) =>
        db.Users.AsNoTracking().Where(user => db.UserRoles.Any(userRole =>
            userRole.UserId == user.Id && userRole.RoleId == IdentityRoleConfiguration.TeacherRoleId));

    private static async Task<IResult> GetAsync(Guid teacherId, HttpContext context, EducationPlatformDbContext db, UserManager<ApplicationUser> users)
    {
        var teacher = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == teacherId, context.RequestAborted);
        if (teacher is null || !await users.IsInRoleAsync(teacher, RoleNames.Teacher)) return NotFound(context);
        return Results.Ok(ToResponse(teacher));
    }

    private static Task<IResult> ApproveAsync(Guid teacherId, HttpContext context, EducationPlatformDbContext db, UserManager<ApplicationUser> users, TimeProvider timeProvider) =>
        TransitionAsync(teacherId, TeacherAccountStatus.Pending, TeacherAccountStatus.Active, false, context, db, users, timeProvider);

    private static Task<IResult> RejectAsync(Guid teacherId, HttpContext context, EducationPlatformDbContext db, UserManager<ApplicationUser> users, TimeProvider timeProvider) =>
        TransitionAsync(teacherId, TeacherAccountStatus.Pending, TeacherAccountStatus.Rejected, false, context, db, users, timeProvider);

    private static Task<IResult> DisableAsync(Guid teacherId, HttpContext context, EducationPlatformDbContext db, UserManager<ApplicationUser> users, TimeProvider timeProvider) =>
        TransitionAsync(teacherId, TeacherAccountStatus.Active, TeacherAccountStatus.Disabled, true, context, db, users, timeProvider);

    private static async Task<IResult> ResetPasswordAsync(
        Guid teacherId,
        ResetTeacherPasswordRequest request,
        HttpContext context,
        EducationPlatformDbContext db,
        UserManager<ApplicationUser> users,
        TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return InvalidNewPassword(context, "A new password is required.");

        var teacher = await db.Users.SingleOrDefaultAsync(user => user.Id == teacherId, context.RequestAborted);
        if (teacher is null || !await users.IsInRoleAsync(teacher, RoleNames.Teacher)) return NotFound(context);

        await using var transaction = await db.Database.BeginTransactionAsync(context.RequestAborted);
        var resetToken = await users.GeneratePasswordResetTokenAsync(teacher);
        var reset = await users.ResetPasswordAsync(teacher, resetToken, request.NewPassword);
        if (!reset.Succeeded)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return InvalidNewPassword(context, string.Join(" ", reset.Errors.Select(error => error.Description)));
        }

        var now = timeProvider.GetUtcNow();
        await db.RefreshTokens.Where(token => token.UserId == teacherId && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, now), context.RequestAborted);
        await transaction.CommitAsync(context.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> TransitionAsync(
        Guid teacherId, TeacherAccountStatus expected, TeacherAccountStatus next, bool revokeTokens,
        HttpContext context, EducationPlatformDbContext db, UserManager<ApplicationUser> users,
        TimeProvider timeProvider)
    {
        var teacher = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == teacherId, context.RequestAborted);
        if (teacher is null || !await users.IsInRoleAsync(teacher, RoleNames.Teacher)) return NotFound(context);

        await using var transaction = await db.Database.BeginTransactionAsync(context.RequestAborted);
        var transitioned = await db.Users
            .Where(user => user.Id == teacherId && user.TeacherAccountStatus == expected)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(user => user.TeacherAccountStatus, next),
                context.RequestAborted);
        if (transitioned == 0)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Result.Failure(new Error("invalid_account_status_transition", "The requested account status transition is not allowed.", ErrorType.Conflict)).ToHttpResult(context);
        }
        if (revokeTokens)
        {
            var now = timeProvider.GetUtcNow();
            await db.RefreshTokens.Where(token => token.UserId == teacherId && token.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, now), context.RequestAborted);
        }
        await transaction.CommitAsync(context.RequestAborted);
        return Results.NoContent();
    }

    private static TeacherAdminResponse ToResponse(ApplicationUser user) => new(
        user.Id, user.Email!, user.Name, user.Surname, user.TeacherAccountStatus!.Value, user.EmailConfirmed);

    private static IResult NotFound(HttpContext context) =>
        Result.Failure(new Error("teacher_not_found", "Teacher account was not found.", ErrorType.NotFound)).ToHttpResult(context);

    private static IResult InvalidNewPassword(HttpContext context, string detail) =>
        Result.Failure(new Error("invalid_new_password", detail, ErrorType.Validation)).ToHttpResult(context);
}

public sealed record ResetTeacherPasswordRequest(string NewPassword);

public sealed record TeacherAdminResponse(
    Guid UserId, string Email, string Name, string? Surname,
    TeacherAccountStatus AccountStatus, bool EmailConfirmed);
