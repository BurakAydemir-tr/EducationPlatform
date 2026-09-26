using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Common.Results;
using EducationPlatform.Api.Features.Learning;
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
        group.MapGet("/", ListAsync);
        group.MapGet("/{classroomId:guid}", GetAsync);
        group.MapGet("/{classroomId:guid}/students", ListStudentsAsync);
        group.MapPost("/{classroomId:guid}/students", AddStudentAsync);
        group.MapDelete("/{classroomId:guid}/students/{studentId:guid}", RemoveStudentAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId)) return AuthenticationFailure(context);
        var classrooms = await db.Classrooms.AsNoTracking()
            .Where(item => item.TeacherId == teacherId)
            .OrderBy(item => item.Name)
            .Select(item => new ClassroomSummaryResponse(
                item.Id,
                item.Name,
                db.ClassroomMemberships.Count(membership => membership.ClassroomId == item.Id && membership.LeftAt == null)))
            .ToListAsync(context.RequestAborted);
        return Results.Ok(classrooms);
    }

    private static async Task<IResult> GetAsync(Guid classroomId, HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId)) return AuthenticationFailure(context);
        var classroom = await db.Classrooms.AsNoTracking()
            .Where(item => item.Id == classroomId)
            .Select(item => new { item.Id, item.Name, item.TeacherId })
            .SingleOrDefaultAsync(context.RequestAborted);
        if (classroom is null) return ClassroomNotFound(context);
        if (classroom.TeacherId != teacherId) return Forbidden(context);
        return Results.Ok(new ClassroomDetailResponse(classroom.Id, classroom.Name));
    }

    private static async Task<IResult> ListStudentsAsync(Guid classroomId, HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId)) return AuthenticationFailure(context);
        var failure = await ValidateOwnershipAsync(classroomId, teacherId, context, db);
        if (failure is not null) return failure;
        var rows = await db.ClassroomMemberships.AsNoTracking()
            .Where(item => item.ClassroomId == classroomId && item.LeftAt == null)
            .Join(db.Users, membership => membership.StudentId, user => user.Id,
                (membership, user) => new
                {
                    user.Id,
                    user.UserName,
                    user.Name,
                    user.StudentCode,
                    membership.JoinedAt
                })
            .OrderBy(item => item.Name)
            .ToListAsync(context.RequestAborted);
        var students = rows.Select(item => new ClassroomStudentResponse(
            item.Id, item.UserName!, item.Name, item.StudentCode, item.JoinedAt)).ToList();
        return Results.Ok(students);
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
        if (request.Name.Trim().Length > 200)
        {
            return Result<CreateClassroomResponse>.Failure(new Error(
                "invalid_classroom", "Classroom name cannot exceed 200 characters.", ErrorType.Validation))
                .ToHttpResult(httpContext);
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

        await using var transaction = await dbContext.Database.BeginTransactionAsync(httpContext.RequestAborted);
        var affectedCourseIds = await dbContext.CourseClassroomAssignments
            .Where(assignment => assignment.ClassroomId == classroomId && assignment.RemovedAt == null)
            .Select(assignment => assignment.CourseId)
            .Distinct()
            .OrderBy(courseId => courseId)
            .ToListAsync(httpContext.RequestAborted);
        if (affectedCourseIds.Count > 0)
        {
            _ = await dbContext.Courses
                .FromSqlInterpolated($"""SELECT * FROM "Courses" WHERE "Id" = ANY ({affectedCourseIds.ToArray()}) ORDER BY "Id" FOR UPDATE""")
                .AsNoTracking()
                .ToListAsync(httpContext.RequestAborted);
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
        await LearningAccess.DeleteIncompleteAttemptsWithoutCourseAccess(
            dbContext, studentId, affectedCourseIds, httpContext.RequestAborted);
        await transaction.CommitAsync(httpContext.RequestAborted);
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

    private static IResult ClassroomNotFound(HttpContext context) =>
        Result.Failure(new Error("classroom_not_found", "Classroom was not found.", ErrorType.NotFound)).ToHttpResult(context);

    private static IResult Forbidden(HttpContext context) =>
        Result.Failure(new Error("forbidden", "You cannot access another teacher's classroom.", ErrorType.Authorization)).ToHttpResult(context);
}

public sealed record CreateClassroomRequest(string Name);

public sealed record CreateClassroomResponse(Guid Id, string Name);

public sealed record ClassroomSummaryResponse(Guid Id, string Name, int ActiveStudentCount);

public sealed record ClassroomDetailResponse(Guid Id, string Name);

public sealed record ClassroomStudentResponse(Guid StudentId, string UserName, string Name, string? StudentCode, DateTimeOffset JoinedAt);

public sealed record AddStudentRequest(string UserNameOrStudentCode);
