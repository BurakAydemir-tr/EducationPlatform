using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Common.Results;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Api.Persistence.Configurations;
using EducationPlatform.Api.Persistence.Courses;
using EducationPlatform.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EducationPlatform.Api.Features.Courses;

public static class CourseEndpoints
{
    public static IEndpointRouteBuilder MapCourseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var teacher = endpoints.MapGroup("/api/courses")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Teacher));
        teacher.MapPost("/", CreateAsync);
        teacher.MapGet("/", ListTeacherCoursesAsync);
        teacher.MapGet("/{courseId:guid}", GetTeacherCourseAsync);
        teacher.MapPost("/{courseId:guid}/weeks", AddWeekAsync);
        teacher.MapPost("/{courseId:guid}/weeks/with-topic", AddPublishedWeekWithTopicAsync);
        teacher.MapPost("/{courseId:guid}/weeks/with-video", AddPublishedWeekWithVideoAsync);
        teacher.MapPost("/{courseId:guid}/weeks/with-quiz", AddPublishedWeekWithQuizAsync);
        teacher.MapPut("/{courseId:guid}/weeks/order", ReorderWeeksAsync);
        teacher.MapPost("/{courseId:guid}/weeks/{weekId:guid}/contents/topic", AddTopicAsync);
        teacher.MapPost("/{courseId:guid}/weeks/{weekId:guid}/contents/video", AddVideoAsync);
        teacher.MapPost("/{courseId:guid}/weeks/{weekId:guid}/contents/quiz", AddQuizAsync);
        teacher.MapPut("/{courseId:guid}/weeks/{weekId:guid}/contents/order", ReorderContentsAsync);
        teacher.MapPut("/{courseId:guid}/weeks/{weekId:guid}/contents/{contentId:guid}/topic", UpdateTopicAsync);
        teacher.MapPut("/{courseId:guid}/weeks/{weekId:guid}/contents/{contentId:guid}/video", UpdateVideoAsync);
        teacher.MapDelete("/{courseId:guid}/weeks/{weekId:guid}/contents/{contentId:guid}", DeactivateContentAsync);
        teacher.MapPost("/{courseId:guid}/classrooms/{classroomId:guid}", AssignClassroomAsync);
        teacher.MapDelete("/{courseId:guid}/classrooms/{classroomId:guid}", RemoveClassroomAsync);
        teacher.MapPost("/{courseId:guid}/publish", PublishAsync);

        var student = endpoints.MapGroup("/api/student/courses")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Student));
        student.MapGet("/", ListStudentCoursesAsync);
        student.MapGet("/{courseId:guid}", GetStudentCourseAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateCourseRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId)) return Authentication(context);
        if (string.IsNullOrWhiteSpace(request.Title)) return Failure(context, "course_title_required", "Course title is required.", ErrorType.Validation);
        var course = new Course(Guid.NewGuid(), request.Title, request.Description, teacherId);
        db.Courses.Add(course);
        await db.SaveChangesAsync(context.RequestAborted);
        return Result<CreateCourseResponse>.Success(new(course.Id, course.Title, course.Description, course.Status))
            .ToCreatedHttpResult(context, $"/api/courses/{course.Id}");
    }

    private static async Task<IResult> ListTeacherCoursesAsync(HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId)) return Authentication(context);
        var courses = await db.Courses.AsNoTracking().Where(course => course.TeacherId == teacherId)
            .OrderBy(course => course.Title)
            .Select(course => new CourseSummaryResponse(course.Id, course.Title, course.Description, course.Status))
            .ToListAsync(context.RequestAborted);
        return Results.Ok(courses);
    }

    private static async Task<IResult> GetTeacherCourseAsync(Guid courseId, HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId)) return Authentication(context);
        var course = await OwnedCourseQuery(db, courseId, teacherId, tracking: false).SingleOrDefaultAsync(context.RequestAborted);
        return course is null ? CourseNotFound(context) : Results.Ok(ToDetail(course));
    }

    private static async Task<IResult> AddWeekAsync(Guid courseId, AddWeekRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        var loaded = await LoadOwnedCourse(courseId, context, db);
        if (loaded.Failure is not null) return loaded.Failure;
        CourseWeek week;
        try
        {
            week = loaded.Course!.AddWeek(Guid.NewGuid(), request.Title, request.Order);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return InvalidOperation(context, exception.Message); }
        db.CourseWeeks.Add(week);
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.Created($"/api/courses/{courseId}/weeks/{week.Id}", new WeekSummaryResponse(week.Id, week.Title, week.Order));
    }

    private static async Task<IResult> AddPublishedWeekWithTopicAsync(Guid courseId, AddPublishedWeekWithTopicRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        var loaded = await LoadOwnedCourse(courseId, context, db);
        if (loaded.Failure is not null) return loaded.Failure;
        CourseWeek week;
        try
        {
            week = loaded.Course!.AddWeekWithTopic(
                Guid.NewGuid(), request.WeekTitle, request.WeekOrder,
                Guid.NewGuid(), request.ContentTitle, 1, request.Text);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return InvalidOperation(context, exception.Message); }
        db.CourseWeeks.Add(week);
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.Created($"/api/courses/{courseId}/weeks/{week.Id}", new WeekSummaryResponse(week.Id, week.Title, week.Order));
    }

    private static async Task<IResult> AddPublishedWeekWithVideoAsync(Guid courseId, AddPublishedWeekWithVideoRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        var loaded = await LoadOwnedCourse(courseId, context, db);
        if (loaded.Failure is not null) return loaded.Failure;
        CourseWeek week;
        try
        {
            week = loaded.Course!.AddWeekWithVideo(
                Guid.NewGuid(), request.WeekTitle, request.WeekOrder,
                Guid.NewGuid(), request.ContentTitle, 1, request.VideoUrl, request.Description);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return InvalidOperation(context, exception.Message); }
        db.CourseWeeks.Add(week);
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.Created($"/api/courses/{courseId}/weeks/{week.Id}", new WeekSummaryResponse(week.Id, week.Title, week.Order));
    }

    private static async Task<IResult> AddPublishedWeekWithQuizAsync(Guid courseId, AddPublishedWeekWithQuizRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        var loaded = await LoadOwnedCourse(courseId, context, db);
        if (loaded.Failure is not null) return loaded.Failure;
        var quizFailure = await ValidateAssignableQuizAsync(loaded.Course!, request.QuizId, context, db);
        if (quizFailure is not null) return quizFailure;
        CourseWeek week;
        try
        {
            week = loaded.Course!.AddWeekWithQuiz(
                Guid.NewGuid(), request.WeekTitle, request.WeekOrder,
                Guid.NewGuid(), request.ContentTitle, 1, request.QuizId);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return InvalidOperation(context, exception.Message); }
        db.CourseWeeks.Add(week);
        try
        {
            await db.SaveChangesAsync(context.RequestAborted);
        }
        catch (DbUpdateException exception) when (IsQuizAlreadyAssigned(exception))
        {
            return QuizAlreadyAssigned(context);
        }
        return Results.Created($"/api/courses/{courseId}/weeks/{week.Id}", new WeekSummaryResponse(week.Id, week.Title, week.Order));
    }

    private static async Task<IResult> AddTopicAsync(Guid courseId, Guid weekId, AddTopicRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        var loaded = await LoadOwnedCourse(courseId, context, db);
        if (loaded.Failure is not null) return loaded.Failure;
        WeekContent content;
        try
        {
            content = loaded.Course!.AddTopic(weekId, Guid.NewGuid(), request.Title, request.Order, request.Text);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return InvalidOperation(context, exception.Message); }
        db.WeekContents.Add(content);
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.Created($"/api/courses/{courseId}/weeks/{weekId}/contents/{content.Id}", ToContent(content));
    }

    private static async Task<IResult> AddVideoAsync(Guid courseId, Guid weekId, AddVideoRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        var loaded = await LoadOwnedCourse(courseId, context, db);
        if (loaded.Failure is not null) return loaded.Failure;
        WeekContent content;
        try
        {
            content = loaded.Course!.AddVideo(weekId, Guid.NewGuid(), request.Title, request.Order, request.VideoUrl, request.Description);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return InvalidOperation(context, exception.Message); }
        db.WeekContents.Add(content);
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.Created($"/api/courses/{courseId}/weeks/{weekId}/contents/{content.Id}", ToContent(content));
    }

    private static async Task<IResult> AddQuizAsync(Guid courseId, Guid weekId, AddQuizContentRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        var loaded = await LoadOwnedCourse(courseId, context, db);
        if (loaded.Failure is not null) return loaded.Failure;
        var quiz = await db.Quizzes.AsNoTracking()
            .Include(item => item.Questions)
            .ThenInclude(question => question.Options)
            .SingleOrDefaultAsync(item => item.Id == request.QuizId, context.RequestAborted);
        if (quiz is null) return Failure(context, "quiz_not_found", "Quiz was not found.", ErrorType.NotFound);
        if (quiz.TeacherId != loaded.Course!.TeacherId) return Forbidden(context);
        if (!quiz.IsValid) return Failure(context, "invalid_quiz", "Quiz is not valid.", ErrorType.Validation);
        if (await db.WeekContents.AnyAsync(content => content.QuizId == request.QuizId, context.RequestAborted))
            return Failure(context, "quiz_already_assigned", "Quiz is already assigned to content.", ErrorType.Conflict);
        WeekContent content;
        try
        {
            content = loaded.Course.AddQuiz(weekId, Guid.NewGuid(), request.Title, request.Order, request.QuizId);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return InvalidOperation(context, exception.Message); }
        db.WeekContents.Add(content);
        try
        {
            await db.SaveChangesAsync(context.RequestAborted);
        }
        catch (DbUpdateException exception) when (IsConstraint(exception, WeekContentConfiguration.QuizContentIndexName))
        {
            return QuizAlreadyAssigned(context);
        }
        return Results.Created($"/api/courses/{courseId}/weeks/{weekId}/contents/{content.Id}", ToContent(content));
    }

    private static async Task<IResult> ReorderWeeksAsync(Guid courseId, ReorderRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        var loaded = await LoadOwnedCourse(courseId, context, db);
        if (loaded.Failure is not null) return loaded.Failure;
        if (request.Ids is null) return InvalidOrder(context, "Week order is required.");
        try
        {
            loaded.Course!.ReorderWeeks(request.Ids);
        }
        catch (InvalidOperationException exception) { return InvalidOrder(context, exception.Message); }
        await PersistWeekReorder(db, courseId, request.Ids, context.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> ReorderContentsAsync(Guid courseId, Guid weekId, ReorderRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        var loaded = await LoadOwnedCourse(courseId, context, db);
        if (loaded.Failure is not null) return loaded.Failure;
        if (request.Ids is null) return InvalidOrder(context, "Content order is required.");
        try
        {
            loaded.Course!.ReorderContents(weekId, request.Ids);
        }
        catch (InvalidOperationException exception) { return InvalidOrder(context, exception.Message); }
        await PersistContentReorder(db, weekId, request.Ids, context.RequestAborted);
        return Results.NoContent();
    }

    private static async Task PersistWeekReorder(EducationPlatformDbContext db, Guid courseId, IReadOnlyList<Guid> orderedIds, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CourseWeeks\" SET \"Order\" = -\"Order\" WHERE \"CourseId\" = {courseId}", token);
        for (var index = 0; index < orderedIds.Count; index++)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CourseWeeks\" SET \"Order\" = {index + 1} WHERE \"Id\" = {orderedIds[index]}", token);
        }
        db.ChangeTracker.Clear();
        await transaction.CommitAsync(token);
    }

    private static async Task PersistContentReorder(EducationPlatformDbContext db, Guid weekId, IReadOnlyList<Guid> orderedIds, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WeekContents\" SET \"Order\" = -\"Order\" WHERE \"CourseWeekId\" = {weekId} AND \"IsActive\" = TRUE", token);
        for (var index = 0; index < orderedIds.Count; index++)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WeekContents\" SET \"Order\" = {index + 1} WHERE \"Id\" = {orderedIds[index]}", token);
        }
        db.ChangeTracker.Clear();
        await transaction.CommitAsync(token);
    }

    private static async Task<IResult> UpdateTopicAsync(Guid courseId, Guid weekId, Guid contentId, UpdateTopicRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        var loaded = await LoadOwnedCourse(courseId, context, db);
        if (loaded.Failure is not null) return loaded.Failure;
        try { loaded.Course!.UpdateTopic(weekId, contentId, request.Title, request.Text); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return InvalidOperation(context, exception.Message); }
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateVideoAsync(Guid courseId, Guid weekId, Guid contentId, UpdateVideoRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        var loaded = await LoadOwnedCourse(courseId, context, db);
        if (loaded.Failure is not null) return loaded.Failure;
        try { loaded.Course!.UpdateVideo(weekId, contentId, request.Title, request.VideoUrl, request.Description); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return InvalidOperation(context, exception.Message); }
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> DeactivateContentAsync(Guid courseId, Guid weekId, Guid contentId, HttpContext context, EducationPlatformDbContext db)
    {
        var loaded = await LoadOwnedCourse(courseId, context, db);
        if (loaded.Failure is not null) return loaded.Failure;
        try
        {
            loaded.Course!.DeactivateContent(weekId, contentId);
        }
        catch (InvalidOperationException exception)
        {
            return InvalidOperation(context, exception.Message);
        }
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> AssignClassroomAsync(Guid courseId, Guid classroomId, HttpContext context, EducationPlatformDbContext db, TimeProvider timeProvider)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId)) return Authentication(context);
        if (!await db.Courses.AnyAsync(course => course.Id == courseId && course.TeacherId == teacherId, context.RequestAborted)) return CourseNotFound(context);
        var classroomOwner = await db.Classrooms.Where(room => room.Id == classroomId).Select(room => (Guid?)room.TeacherId).SingleOrDefaultAsync(context.RequestAborted);
        if (classroomOwner is null) return Failure(context, "classroom_not_found", "Classroom was not found.", ErrorType.NotFound);
        if (classroomOwner != teacherId) return Forbidden(context);
        if (await db.CourseClassroomAssignments.AnyAsync(a => a.CourseId == courseId && a.ClassroomId == classroomId && a.RemovedAt == null, context.RequestAborted)) return DuplicateAssignment(context);
        db.CourseClassroomAssignments.Add(new CourseClassroomAssignment { Id = Guid.NewGuid(), CourseId = courseId, ClassroomId = classroomId, AssignedAt = timeProvider.GetUtcNow() });
        try { await db.SaveChangesAsync(context.RequestAborted); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres && postgres.ConstraintName == CourseClassroomAssignment.ActiveAssignmentIndexName) { return DuplicateAssignment(context); }
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveClassroomAsync(Guid courseId, Guid classroomId, HttpContext context, EducationPlatformDbContext db, TimeProvider timeProvider)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId)) return Authentication(context);
        if (!await db.Courses.AnyAsync(course => course.Id == courseId && course.TeacherId == teacherId, context.RequestAborted)) return CourseNotFound(context);
        var assignment = await db.CourseClassroomAssignments.SingleOrDefaultAsync(a => a.CourseId == courseId && a.ClassroomId == classroomId && a.RemovedAt == null, context.RequestAborted);
        if (assignment is null) return Failure(context, "course_assignment_not_found", "Active course assignment was not found.", ErrorType.NotFound);
        assignment.RemovedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> PublishAsync(Guid courseId, HttpContext context, EducationPlatformDbContext db)
    {
        var loaded = await LoadOwnedCourse(courseId, context, db);
        if (loaded.Failure is not null) return loaded.Failure;
        var quizIds = loaded.Course!.Weeks.SelectMany(week => week.Contents).Where(content => content.IsActive && content.Type == WeekContentType.Quiz).Select(content => content.QuizId!.Value).Distinct().ToList();
        if (quizIds.Count > 0)
        {
            var quizzes = await db.Quizzes.Include(quiz => quiz.Questions).ThenInclude(question => question.Options).Where(quiz => quizIds.Contains(quiz.Id) && quiz.TeacherId == loaded.Course.TeacherId).ToListAsync(context.RequestAborted);
            if (quizzes.Count != quizIds.Count || quizzes.Any(quiz => !quiz.IsValid)) return Failure(context, "course_not_publishable", "Every quiz content must reference a valid quiz owned by the teacher.", ErrorType.Conflict);
        }
        try { loaded.Course.Publish(); }
        catch (InvalidOperationException exception) { return Failure(context, "course_not_publishable", exception.Message, ErrorType.Conflict); }
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> ListStudentCoursesAsync(HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var studentId)) return Authentication(context);
        var courses = await AccessibleCourses(db, studentId).OrderBy(course => course.Title).Select(course => new CourseSummaryResponse(course.Id, course.Title, course.Description, course.Status)).ToListAsync(context.RequestAborted);
        return Results.Ok(courses);
    }

    private static async Task<IResult> GetStudentCourseAsync(Guid courseId, HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var studentId)) return Authentication(context);
        var accessible = await AccessibleCourses(db, studentId).AnyAsync(course => course.Id == courseId, context.RequestAborted);
        if (!accessible) return Failure(context, "course_not_accessible", "Course is not accessible.", ErrorType.NotFound);
        var course = await db.Courses.AsNoTracking().Include(item => item.Weeks).ThenInclude(week => week.Contents).SingleAsync(item => item.Id == courseId, context.RequestAborted);
        return Results.Ok(ToDetail(course, activeOnly: true));
    }

    private static IQueryable<Course> AccessibleCourses(EducationPlatformDbContext db, Guid studentId) =>
        db.Courses.AsNoTracking().Where(course => course.Status == CourseStatus.Published
            && db.CourseClassroomAssignments.Any(assignment => assignment.CourseId == course.Id && assignment.RemovedAt == null
                && db.ClassroomMemberships.Any(membership => membership.ClassroomId == assignment.ClassroomId && membership.StudentId == studentId && membership.LeftAt == null)));

    private static async Task<(Course? Course, IResult? Failure)> LoadOwnedCourse(Guid courseId, HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId)) return (null, Authentication(context));
        var owner = await db.Courses.Where(course => course.Id == courseId).Select(course => (Guid?)course.TeacherId).SingleOrDefaultAsync(context.RequestAborted);
        if (owner is null) return (null, CourseNotFound(context));
        if (owner != teacherId) return (null, Forbidden(context));
        var course = await OwnedCourseQuery(db, courseId, teacherId, tracking: true).SingleAsync(context.RequestAborted);
        return (course, null);
    }

    private static IQueryable<Course> OwnedCourseQuery(EducationPlatformDbContext db, Guid id, Guid teacherId, bool tracking)
    {
        var query = db.Courses.Include(course => course.Weeks).ThenInclude(week => week.Contents).Where(course => course.Id == id && course.TeacherId == teacherId);
        return tracking ? query : query.AsNoTracking();
    }

    private static async Task<IResult?> ValidateAssignableQuizAsync(
        Course course,
        Guid quizId,
        HttpContext context,
        EducationPlatformDbContext db)
    {
        var quiz = await db.Quizzes.AsNoTracking()
            .Include(item => item.Questions)
            .ThenInclude(question => question.Options)
            .SingleOrDefaultAsync(item => item.Id == quizId, context.RequestAborted);
        if (quiz is null) return Failure(context, "quiz_not_found", "Quiz was not found.", ErrorType.NotFound);
        if (quiz.TeacherId != course.TeacherId) return Forbidden(context);
        if (!quiz.IsValid) return Failure(context, "invalid_quiz", "Quiz is not valid.", ErrorType.Validation);
        if (await db.WeekContents.AnyAsync(content => content.QuizId == quizId, context.RequestAborted))
            return QuizAlreadyAssigned(context);
        return null;
    }

    private static bool IsQuizAlreadyAssigned(DbUpdateException exception) =>
        IsConstraint(exception, WeekContentConfiguration.QuizContentIndexName);

    private static bool IsConstraint(DbUpdateException exception, string constraintName) =>
        exception.InnerException is PostgresException postgres
        && postgres.ConstraintName == constraintName;

    private static CourseDetailResponse ToDetail(Course course, bool activeOnly = false) => new(course.Id, course.Title, course.Description, course.Status,
        course.Weeks.OrderBy(week => week.Order).Select(week => new CourseWeekResponse(week.Id, week.Title, week.Order,
            week.Contents.Where(content => !activeOnly || content.IsActive).OrderBy(content => content.Order).Select(ToContent).ToList())).ToList());
    private static WeekContentResponse ToContent(WeekContent content) => new(content.Id, content.Title, content.Order, content.Type, content.TopicText, content.VideoUrl, content.VideoDescription, content.QuizId, content.IsActive);
    private static IResult Authentication(HttpContext context) => Failure(context, "authentication_required", "Authentication is required.", ErrorType.Authentication);
    private static IResult Forbidden(HttpContext context) => Failure(context, "forbidden", "You cannot manage another teacher's resource.", ErrorType.Authorization);
    private static IResult CourseNotFound(HttpContext context) => Failure(context, "course_not_found", "Course was not found.", ErrorType.NotFound);
    private static IResult DuplicateAssignment(HttpContext context) => Failure(context, "duplicate_course_assignment", "Course is already assigned to the classroom.", ErrorType.Conflict);
    private static IResult QuizAlreadyAssigned(HttpContext context) => Failure(context, "quiz_already_assigned", "Quiz is already assigned to content.", ErrorType.Conflict);
    private static IResult InvalidOrder(HttpContext context, string detail) => Failure(context, "invalid_order", detail, ErrorType.Validation);
    private static IResult InvalidOperation(HttpContext context, string detail) => Failure(context, "invalid_operation", detail, ErrorType.Validation);
    private static IResult Failure(HttpContext context, string code, string detail, ErrorType type) => Result.Failure(new Error(code, detail, type)).ToHttpResult(context);
}

public sealed record CreateCourseRequest(string Title, string? Description);
public sealed record CreateCourseResponse(Guid Id, string Title, string? Description, CourseStatus Status);
public sealed record CourseSummaryResponse(Guid Id, string Title, string? Description, CourseStatus Status);
public sealed record AddWeekRequest(string Title, int Order);
public sealed record AddPublishedWeekWithTopicRequest(string WeekTitle, int WeekOrder, string ContentTitle, string Text);
public sealed record AddPublishedWeekWithVideoRequest(string WeekTitle, int WeekOrder, string ContentTitle, string VideoUrl, string? Description);
public sealed record AddPublishedWeekWithQuizRequest(string WeekTitle, int WeekOrder, string ContentTitle, Guid QuizId);
public sealed record WeekSummaryResponse(Guid Id, string Title, int Order);
public sealed record AddTopicRequest(string Title, int Order, string Text);
public sealed record AddVideoRequest(string Title, int Order, string VideoUrl, string? Description);
public sealed record AddQuizContentRequest(string Title, int Order, Guid QuizId);
public sealed record UpdateTopicRequest(string Title, string Text);
public sealed record UpdateVideoRequest(string Title, string VideoUrl, string? Description);
public sealed record ReorderRequest(IReadOnlyList<Guid> Ids);
public sealed record CourseDetailResponse(Guid Id, string Title, string? Description, CourseStatus Status, IReadOnlyList<CourseWeekResponse> Weeks);
public sealed record CourseWeekResponse(Guid Id, string Title, int Order, IReadOnlyList<WeekContentResponse> Contents);
public sealed record WeekContentResponse(Guid Id, string Title, int Order, WeekContentType Type, string? TopicText, string? VideoUrl, string? VideoDescription, Guid? QuizId, bool IsActive);
