using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Common.Results;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Api.Persistence.Configurations;
using EducationPlatform.Domain.Courses;
using EducationPlatform.Domain.Progress;
using EducationPlatform.Domain.Quizzes;
using EducationPlatform.Domain.QuizAttempts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EducationPlatform.Api.Features.Learning;

public static class LearningEndpoints
{
    public static IEndpointRouteBuilder MapLearningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var student = endpoints.MapGroup("/api/student").RequireAuthorization(policy => policy.RequireRole(RoleNames.Student));
        student.MapPost("/quizzes/{quizId:guid}/attempts", StartAttemptAsync);
        student.MapPost("/quiz-attempts/{attemptId:guid}/complete", CompleteAttemptAsync);
        student.MapGet("/quiz-attempts/{attemptId:guid}", GetAttemptAsync);
        student.MapPost("/contents/{contentId:guid}/complete", CompleteContentAsync);
        student.MapGet("/courses/{courseId:guid}/progress", GetStudentProgressAsync);

        var teacher = endpoints.MapGroup("/api/courses").RequireAuthorization(policy => policy.RequireRole(RoleNames.Teacher));
        teacher.MapGet("/{courseId:guid}/students/{studentId:guid}/progress", GetTeacherProgressAsync);
        teacher.MapGet("/{courseId:guid}/students/{studentId:guid}/quiz-results", GetTeacherQuizResultsAsync);
        return endpoints;
    }

    private static async Task<IResult> StartAttemptAsync(Guid quizId, HttpContext context, EducationPlatformDbContext db, TimeProvider timeProvider)
    {
        if (!CurrentUser.TryGetId(context.User, out var studentId)) return Authentication(context);
        var accessible = await LearningAccess.AccessibleContents(db, studentId)
            .AnyAsync(item => item.Type == WeekContentType.Quiz && item.QuizId == quizId, context.RequestAborted);
        if (!accessible) return Failure(context, "quiz_not_accessible", "Quiz is not accessible.", ErrorType.NotFound);

        var quiz = await db.Quizzes.Include(item => item.Questions).ThenInclude(question => question.Options)
            .SingleAsync(item => item.Id == quizId, context.RequestAborted);
        if (!quiz.IsValid) return Failure(context, "invalid_quiz", "Quiz is not valid.", ErrorType.Conflict);

        quiz.Lock();
        var attempt = new QuizAttempt(Guid.NewGuid(), studentId, quizId, timeProvider.GetUtcNow());
        db.QuizAttempts.Add(attempt);
        await db.SaveChangesAsync(context.RequestAborted);

        return Results.Created($"/api/student/quiz-attempts/{attempt.Id}", new StartQuizAttemptResponse(
            attempt.Id,
            attempt.StartedAt,
            quiz.Title,
            quiz.Questions.OrderBy(question => question.Order)
                .Select(question => new QuizQuestionResponse(question.Id, question.Text, question.Order,
                    question.Options.OrderBy(option => option.Order)
                        .Select(option => new QuizOptionResponse(option.Id, option.Text, option.Order)).ToList()))
                .ToList()));
    }

    private static async Task<IResult> CompleteAttemptAsync(Guid attemptId, CompleteQuizAttemptRequest request, HttpContext context, EducationPlatformDbContext db, TimeProvider timeProvider)
    {
        if (!CurrentUser.TryGetId(context.User, out var studentId)) return Authentication(context);
        var attempt = await db.QuizAttempts.Include(item => item.Answers)
            .SingleOrDefaultAsync(item => item.Id == attemptId && item.StudentId == studentId, context.RequestAborted);
        if (attempt is null) return Failure(context, "quiz_attempt_not_found", "Quiz attempt was not found.", ErrorType.NotFound);
        if (attempt.IsCompleted) return Failure(context, "quiz_attempt_already_completed", "Quiz attempt is already completed.", ErrorType.Conflict);

        var contentId = await LearningAccess.AccessibleContents(db, studentId)
            .Where(item => item.Type == WeekContentType.Quiz && item.QuizId == attempt.QuizId)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(context.RequestAborted);
        if (contentId is null) return Failure(context, "quiz_not_accessible", "Quiz is not accessible.", ErrorType.NotFound);

        var quiz = await db.Quizzes.AsNoTracking().Include(item => item.Questions).ThenInclude(question => question.Options)
            .SingleAsync(item => item.Id == attempt.QuizId, context.RequestAborted);
        if (!TryEvaluate(quiz, request.Answers, out var selections, out var correctCount, out var error))
            return Failure(context, "invalid_quiz_answers", error!, ErrorType.Validation);

        var score = correctCount * 100m / quiz.Questions.Count;
        try
        {
            attempt.Complete(selections!, correctCount, quiz.Questions.Count, score, timeProvider.GetUtcNow());
            db.QuizAttemptAnswers.AddRange(attempt.Answers);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(context, "quiz_attempt_already_completed", exception.Message, ErrorType.Conflict);
        }

        ContentProgress? progress = null;
        if (!await db.ContentProgress.AnyAsync(item => item.StudentId == studentId && item.ContentId == contentId.Value, context.RequestAborted))
        {
            progress = new ContentProgress(studentId, contentId.Value, attempt.CompletedAt!.Value);
            db.ContentProgress.Add(progress);
        }

        try
        {
            await db.SaveChangesAsync(context.RequestAborted);
        }
        catch (DbUpdateException exception) when (progress is not null && IsProgressDuplicate(exception))
        {
            db.Entry(progress).State = EntityState.Detached;
            await db.SaveChangesAsync(context.RequestAborted);
        }

        return Results.Ok(ToAttemptResult(attempt));
    }

    private static async Task<IResult> GetAttemptAsync(Guid attemptId, HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var studentId)) return Authentication(context);
        var attempt = await db.QuizAttempts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == attemptId && item.StudentId == studentId, context.RequestAborted);
        if (attempt is null) return Failure(context, "quiz_attempt_not_found", "Quiz attempt was not found.", ErrorType.NotFound);
        return attempt.IsCompleted
            ? Results.Ok(ToAttemptResult(attempt))
            : Failure(context, "quiz_attempt_not_completed", "Quiz attempt is not completed.", ErrorType.Conflict);
    }

    private static async Task<IResult> CompleteContentAsync(Guid contentId, HttpContext context, EducationPlatformDbContext db, TimeProvider timeProvider)
    {
        if (!CurrentUser.TryGetId(context.User, out var studentId)) return Authentication(context);
        var content = await LearningAccess.AccessibleContents(db, studentId)
            .SingleOrDefaultAsync(item => item.Id == contentId, context.RequestAborted);
        if (content is null) return Failure(context, "content_not_accessible", "Content is not accessible.", ErrorType.NotFound);
        if (content.Type == WeekContentType.Quiz)
            return Failure(context, "content_cannot_be_completed_directly", "Quiz content is completed by completing a quiz attempt.", ErrorType.Validation);

        if (await db.ContentProgress.AnyAsync(item => item.StudentId == studentId && item.ContentId == contentId, context.RequestAborted))
            return Results.NoContent();

        var progress = new ContentProgress(studentId, contentId, timeProvider.GetUtcNow());
        db.ContentProgress.Add(progress);
        try
        {
            await db.SaveChangesAsync(context.RequestAborted);
        }
        catch (DbUpdateException exception) when (IsProgressDuplicate(exception))
        {
            db.Entry(progress).State = EntityState.Detached;
        }
        return Results.NoContent();
    }

    private static async Task<IResult> GetStudentProgressAsync(Guid courseId, HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var studentId)) return Authentication(context);
        if (!await LearningAccess.AccessibleCourses(db, studentId).AnyAsync(course => course.Id == courseId, context.RequestAborted))
            return Failure(context, "course_not_accessible", "Course is not accessible.", ErrorType.NotFound);
        return Results.Ok(await BuildProgress(db, courseId, studentId, context.RequestAborted));
    }

    private static async Task<IResult> GetTeacherProgressAsync(Guid courseId, Guid studentId, HttpContext context, EducationPlatformDbContext db)
    {
        var failure = await ValidateTeacherReportAccess(courseId, studentId, context, db);
        return failure ?? Results.Ok(await BuildProgress(db, courseId, studentId, context.RequestAborted));
    }

    private static async Task<IResult> GetTeacherQuizResultsAsync(Guid courseId, Guid studentId, HttpContext context, EducationPlatformDbContext db)
    {
        var failure = await ValidateTeacherReportAccess(courseId, studentId, context, db);
        if (failure is not null) return failure;

        var quizIds = await (from content in db.WeekContents
            join week in db.CourseWeeks on EF.Property<Guid>(content, "CourseWeekId") equals week.Id
            where EF.Property<Guid>(week, "CourseId") == courseId && content.QuizId != null
            select content.QuizId!.Value).Distinct().ToListAsync(context.RequestAborted);
        var attempts = await db.QuizAttempts.AsNoTracking()
            .Where(item => item.StudentId == studentId && item.CompletedAt != null && quizIds.Contains(item.QuizId))
            .OrderBy(item => item.CompletedAt).ThenBy(item => item.Id)
            .Select(item => new { item.QuizId, item.Score, item.CompletedAt })
            .ToListAsync(context.RequestAborted);
        var titles = await db.Quizzes.AsNoTracking().Where(item => quizIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Title, context.RequestAborted);
        var results = attempts.GroupBy(item => item.QuizId).Select(group =>
        {
            var ordered = group.ToList();
            return new TeacherQuizResultResponse(group.Key, titles[group.Key], ordered.Count, ordered[0].Score,
                ordered[^1].Score, ordered.Max(item => item.Score), ordered[^1].CompletedAt!.Value);
        }).ToList();
        return Results.Ok(results);
    }

    private static async Task<CourseProgressResponse> BuildProgress(EducationPlatformDbContext db, Guid courseId, Guid studentId, CancellationToken token)
    {
        var contents = await (from content in db.WeekContents.AsNoTracking()
            join week in db.CourseWeeks.AsNoTracking() on EF.Property<Guid>(content, "CourseWeekId") equals week.Id
            where EF.Property<Guid>(week, "CourseId") == courseId && content.IsActive
            orderby week.Order, content.Order
            select new { WeekId = week.Id, WeekTitle = week.Title, WeekOrder = week.Order, ContentId = content.Id }).ToListAsync(token);
        var contentIds = contents.Select(item => item.ContentId).ToList();
        var completedIds = await db.ContentProgress.AsNoTracking()
            .Where(item => item.StudentId == studentId && contentIds.Contains(item.ContentId))
            .Select(item => item.ContentId).ToHashSetAsync(token);
        var weeks = contents.GroupBy(item => new { item.WeekId, item.WeekTitle, item.WeekOrder })
            .Select(group => new WeekProgressResponse(group.Key.WeekId, group.Key.WeekTitle, group.Key.WeekOrder,
                group.Count(item => completedIds.Contains(item.ContentId)), group.Count(), Percentage(group.Count(item => completedIds.Contains(item.ContentId)), group.Count())))
            .OrderBy(item => item.Order).ToList();
        return new CourseProgressResponse(courseId, completedIds.Count, contents.Count, Percentage(completedIds.Count, contents.Count), weeks);
    }

    private static async Task<IResult?> ValidateTeacherReportAccess(Guid courseId, Guid studentId, HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId)) return Authentication(context);
        var owner = await db.Courses.Where(item => item.Id == courseId).Select(item => (Guid?)item.TeacherId).SingleOrDefaultAsync(context.RequestAborted);
        if (owner is null) return Failure(context, "course_not_found", "Course was not found.", ErrorType.NotFound);
        if (owner != teacherId) return Failure(context, "forbidden", "You cannot view another teacher's course reports.", ErrorType.Authorization);
        var related = await db.CourseClassroomAssignments.AnyAsync(assignment => assignment.CourseId == courseId
            && db.ClassroomMemberships.Any(membership => membership.ClassroomId == assignment.ClassroomId && membership.StudentId == studentId), context.RequestAborted);
        return related ? null : Failure(context, "student_not_in_course_context", "Student is not in this course context.", ErrorType.NotFound);
    }

    private static bool TryEvaluate(Quiz quiz, IReadOnlyList<QuizAnswerRequest>? answers, out IReadOnlyCollection<QuizAttemptAnswerSelection>? selections, out int correctCount, out string? error)
    {
        selections = null; correctCount = 0; error = null;
        if (answers is null || answers.Count != quiz.Questions.Count || answers.Select(item => item.QuestionId).Distinct().Count() != answers.Count)
        { error = "Every question must have exactly one answer."; return false; }
        var result = new List<QuizAttemptAnswerSelection>();
        foreach (var question in quiz.Questions)
        {
            var submitted = answers.SingleOrDefault(item => item.QuestionId == question.Id);
            if (submitted is null) { error = "Every question must have exactly one answer."; return false; }
            var option = question.Options.SingleOrDefault(item => item.Id == submitted.SelectedOptionId);
            if (option is null) { error = "Selected option does not belong to the question."; return false; }
            if (option.IsCorrect) correctCount++;
            result.Add(new QuizAttemptAnswerSelection(question.Id, option.Id));
        }
        selections = result;
        return true;
    }

    private static decimal Percentage(int completed, int total) => total == 0 ? 0 : completed * 100m / total;
    private static QuizAttemptResultResponse ToAttemptResult(QuizAttempt attempt) => new(attempt.Id, attempt.QuizId, attempt.StartedAt, attempt.CompletedAt!.Value, attempt.CorrectAnswerCount, attempt.QuestionCount, attempt.Score);
    private static bool IsProgressDuplicate(DbUpdateException exception) => exception.InnerException is PostgresException postgres && postgres.ConstraintName == ContentProgressConfiguration.PrimaryKeyName;
    private static IResult Authentication(HttpContext context) => Failure(context, "authentication_required", "Authentication is required.", ErrorType.Authentication);
    private static IResult Failure(HttpContext context, string code, string detail, ErrorType type) => Result.Failure(new Error(code, detail, type)).ToHttpResult(context);
}

public sealed record StartQuizAttemptResponse(Guid AttemptId, DateTimeOffset StartedAt, string QuizTitle, IReadOnlyList<QuizQuestionResponse> Questions);
public sealed record QuizQuestionResponse(Guid Id, string Text, int Order, IReadOnlyList<QuizOptionResponse> Options);
public sealed record QuizOptionResponse(Guid Id, string Text, int Order);
public sealed record CompleteQuizAttemptRequest(IReadOnlyList<QuizAnswerRequest>? Answers);
public sealed record QuizAnswerRequest(Guid QuestionId, Guid SelectedOptionId);
public sealed record QuizAttemptResultResponse(Guid AttemptId, Guid QuizId, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, int CorrectAnswerCount, int QuestionCount, decimal Score);
public sealed record CourseProgressResponse(Guid CourseId, int CompletedContentCount, int TotalContentCount, decimal Percentage, IReadOnlyList<WeekProgressResponse> Weeks);
public sealed record WeekProgressResponse(Guid WeekId, string Title, int Order, int CompletedContentCount, int TotalContentCount, decimal Percentage);
public sealed record TeacherQuizResultResponse(Guid QuizId, string QuizTitle, int AttemptCount, decimal FirstScore, decimal LastScore, decimal BestScore, DateTimeOffset LastAttemptAt);
