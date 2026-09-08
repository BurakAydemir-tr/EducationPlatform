using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Common.Results;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Api.Persistence.Classrooms;
using EducationPlatform.Api.Persistence.Identity;
using EducationPlatform.Domain.Classrooms;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EducationPlatform.Api.Features.Classrooms;

public static class ClassroomEndpoints
{
    public static IEndpointRouteBuilder MapClassroomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/classrooms")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Teacher));

        group.MapPost("/", CreateAsync);
        group.MapPost("/{classroomId:guid}/students", AddStudentAsync);
        group.MapDelete("/{classroomId:guid}/students/{studentId:guid}", RemoveStudentAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateClassroomRequest request,
        HttpContext httpContext,
        EducationPlatformDbContext dbContext)
    {
        if (!CurrentUser.TryGetId(httpContext.User, out var teacherId))
        {
            return AuthenticationFailure(httpContext);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<CreateClassroomResponse>.Failure(new Error(
                "classroom_name_required",
                "Classroom name is required.",
                ErrorType.Validation)).ToHttpResult(httpContext);
        }

        var classroom = new Classroom(Guid.NewGuid(), request.Name, teacherId);
        dbContext.Classrooms.Add(classroom);
        await dbContext.SaveChangesAsync(httpContext.RequestAborted);

        var response = new CreateClassroomResponse(classroom.Id, classroom.Name);
        return Result<CreateClassroomResponse>.Success(response)
            .ToCreatedHttpResult(httpContext, $"/api/classrooms/{classroom.Id}");
    }

    private static async Task<IResult> AddStudentAsync(
        Guid classroomId,
        AddStudentRequest request,
        HttpContext httpContext,
        EducationPlatformDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        TimeProvider timeProvider)
    {
        if (!CurrentUser.TryGetId(httpContext.User, out var teacherId))
        {
            return AuthenticationFailure(httpContext);
        }

        if (string.IsNullOrWhiteSpace(request.UserNameOrStudentCode))
        {
            return Result.Failure(new Error(
                "student_identifier_required",
                "Username or student code is required.",
                ErrorType.Validation)).ToHttpResult(httpContext);
        }

        var ownershipFailure = await ValidateOwnershipAsync(classroomId, teacherId, httpContext, dbContext);
        if (ownershipFailure is not null)
        {
            return ownershipFailure;
        }

        var identifier = request.UserNameOrStudentCode.Trim();
        var student = await userManager.FindByNameAsync(identifier)
            ?? await dbContext.Users.SingleOrDefaultAsync(
                user => user.StudentCode == identifier,
                httpContext.RequestAborted);

        if (student is null)
        {
            return Result.Failure(new Error(
                "student_not_found",
                "Student was not found.",
                ErrorType.NotFound)).ToHttpResult(httpContext);
        }

        if (!await userManager.IsInRoleAsync(student, RoleNames.Student))
        {
            return Result.Failure(new Error(
                "user_is_not_student",
                "Only Student users can be added to a classroom.",
                ErrorType.Validation)).ToHttpResult(httpContext);
        }

        var alreadyMember = await dbContext.ClassroomMemberships.AnyAsync(
            membership => membership.ClassroomId == classroomId
                && membership.StudentId == student.Id
                && membership.LeftAt == null,
            httpContext.RequestAborted);
        if (alreadyMember)
        {
            return DuplicateMembership(httpContext);
        }

        dbContext.ClassroomMemberships.Add(new ClassroomMembership
        {
            Id = Guid.NewGuid(),
            ClassroomId = classroomId,
            StudentId = student.Id,
            JoinedAt = timeProvider.GetUtcNow()
        });

        try
        {
            await dbContext.SaveChangesAsync(httpContext.RequestAborted);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException postgresException
            && postgresException.ConstraintName == ClassroomMembership.ActiveMembershipIndexName)
        {
            return DuplicateMembership(httpContext);
        }

        return Result.Success().ToHttpResult(httpContext);
    }

    private static async Task<IResult> RemoveStudentAsync(
        Guid classroomId,
        Guid studentId,
        HttpContext httpContext,
        EducationPlatformDbContext dbContext,
        TimeProvider timeProvider)
    {
        if (!CurrentUser.TryGetId(httpContext.User, out var teacherId))
        {
            return AuthenticationFailure(httpContext);
        }

        var ownershipFailure = await ValidateOwnershipAsync(classroomId, teacherId, httpContext, dbContext);
        if (ownershipFailure is not null)
        {
            return ownershipFailure;
        }

        var membership = await dbContext.ClassroomMemberships.SingleOrDefaultAsync(
            item => item.ClassroomId == classroomId
                && item.StudentId == studentId
                && item.LeftAt == null,
            httpContext.RequestAborted);
        if (membership is null)
        {
            return Result.Failure(new Error(
                "membership_not_found",
                "The student is not an active member of this classroom.",
                ErrorType.NotFound)).ToHttpResult(httpContext);
        }

        membership.LeftAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(httpContext.RequestAborted);
        return Result.Success().ToHttpResult(httpContext);
    }

    private static async Task<IResult?> ValidateOwnershipAsync(
        Guid classroomId,
        Guid teacherId,
        HttpContext httpContext,
        EducationPlatformDbContext dbContext)
    {
        var ownerId = await dbContext.Classrooms
            .Where(classroom => classroom.Id == classroomId)
            .Select(classroom => (Guid?)classroom.TeacherId)
            .SingleOrDefaultAsync(httpContext.RequestAborted);

        if (ownerId is null)
        {
            return Result.Failure(new Error(
                "classroom_not_found",
                "Classroom was not found.",
                ErrorType.NotFound)).ToHttpResult(httpContext);
        }

        return ownerId != teacherId
            ? Result.Failure(new Error(
                "forbidden",
                "You cannot manage another teacher's classroom.",
                ErrorType.Authorization)).ToHttpResult(httpContext)
            : null;
    }

    private static IResult AuthenticationFailure(HttpContext httpContext) =>
        Result.Failure(new Error(
            "authentication_required",
            "Authentication is required.",
            ErrorType.Authentication)).ToHttpResult(httpContext);

    private static IResult DuplicateMembership(HttpContext httpContext) =>
        Result.Failure(new Error(
            "duplicate_membership",
            "The student is already an active member of this classroom.",
            ErrorType.Conflict)).ToHttpResult(httpContext);
}

public sealed record CreateClassroomRequest(string Name);

public sealed record CreateClassroomResponse(Guid Id, string Name);

public sealed record AddStudentRequest(string UserNameOrStudentCode);
