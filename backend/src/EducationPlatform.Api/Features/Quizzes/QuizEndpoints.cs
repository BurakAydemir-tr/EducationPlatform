using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Common.Results;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Domain.Quizzes;
using Microsoft.EntityFrameworkCore;

namespace EducationPlatform.Api.Features.Quizzes;

public static class QuizEndpoints
{
    public static IEndpointRouteBuilder MapQuizEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/quizzes")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Teacher));
        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{quizId:guid}", GetAsync);
        group.MapPut("/{quizId:guid}", UpdateAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateQuizRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId))
            return Failure(context, "authentication_required", "Authentication is required.", ErrorType.Authentication);
        var validation = Validate(request.Title, request.Questions);
        if (validation is not null) return Failure(context, "invalid_quiz", validation, ErrorType.Validation);
        Quiz quiz;
        try
        {
            quiz = new Quiz(Guid.NewGuid(), request.Title, teacherId);
            foreach (var questionRequest in request.Questions.Select(question => question!).OrderBy(question => question.Order))
            {
                var question = quiz.AddQuestion(Guid.NewGuid(), questionRequest.Text, questionRequest.Order);
                foreach (var option in questionRequest.Options.Select(item => item!).OrderBy(item => item.Order))
                    quiz.AddOption(question.Id, Guid.NewGuid(), option.Text, option.Order, option.IsCorrect);
            }
            if (!quiz.IsValid)
                return Failure(context, "invalid_quiz", "Quiz must contain a question and every question must have at least two options with exactly one correct option.", ErrorType.Validation);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Failure(context, "invalid_quiz", exception.Message, ErrorType.Validation);
        }
        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.Created($"/api/quizzes/{quiz.Id}", new CreateQuizResponse(quiz.Id, quiz.Title));
    }

    private static async Task<IResult> GetAsync(Guid quizId, HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId))
            return Failure(context, "authentication_required", "Authentication is required.", ErrorType.Authentication);
        var quiz = await db.Quizzes.AsNoTracking().Include(item => item.Questions).ThenInclude(item => item.Options)
            .SingleOrDefaultAsync(item => item.Id == quizId, context.RequestAborted);
        if (quiz is null) return Failure(context, "quiz_not_found", "Quiz was not found.", ErrorType.NotFound);
        if (quiz.TeacherId != teacherId) return Failure(context, "forbidden", "You cannot access another teacher's quiz.", ErrorType.Authorization);
        return Results.Ok(ToResponse(quiz));
    }

    private static async Task<IResult> ListAsync(HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId))
            return Failure(context, "authentication_required", "Authentication is required.", ErrorType.Authentication);
        var quizzes = await db.Quizzes.AsNoTracking().Where(quiz => quiz.TeacherId == teacherId)
            .OrderBy(quiz => quiz.Title).ThenBy(quiz => quiz.Id)
            .Select(quiz => new QuizSummaryResponse(quiz.Id, quiz.Title, quiz.IsLocked,
                db.WeekContents.Any(content => content.QuizId == quiz.Id)))
            .ToListAsync(context.RequestAborted);
        return Results.Ok(quizzes);
    }

    private static async Task<IResult> UpdateAsync(Guid quizId, UpdateQuizRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId))
            return Failure(context, "authentication_required", "Authentication is required.", ErrorType.Authentication);
        var validation = Validate(request.Title, request.Questions);
        if (validation is not null) return Failure(context, "invalid_quiz", validation, ErrorType.Validation);
        await using var transaction = await db.Database.BeginTransactionAsync(context.RequestAborted);
        var quiz = await db.Quizzes
            .FromSqlInterpolated($"""SELECT * FROM "Quizzes" WHERE "Id" = {quizId} FOR UPDATE""")
            .SingleOrDefaultAsync(context.RequestAborted);
        if (quiz is null) return Failure(context, "quiz_not_found", "Quiz was not found.", ErrorType.NotFound);
        await db.Entry(quiz).Collection(item => item.Questions).Query()
            .Include(item => item.Options)
            .LoadAsync(context.RequestAborted);
        if (quiz.TeacherId != teacherId) return Failure(context, "forbidden", "You cannot manage another teacher's quiz.", ErrorType.Authorization);
        if (quiz.IsLocked) return Failure(context, "quiz_locked", "Quiz cannot be changed after an attempt has started.", ErrorType.Conflict);
        var replacedQuestions = quiz.Questions.ToList();
        try
        {
            quiz.Update(request.Title, request.Questions.Select(question => new QuizQuestionDefinition(
                question!.Text, question.Order,
                question.Options.Select(option => new QuizOptionDefinition(option!.Text, option.Order, option.IsCorrect)).ToList())).ToList());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Failure(context, "invalid_quiz", exception.Message, ErrorType.Validation);
        }

        var replacedQuestionIds = replacedQuestions.Select(item => item.Id).ToList();
        await db.Set<Option>().Where(option => replacedQuestionIds.Contains(EF.Property<Guid>(option, "QuestionId")))
            .ExecuteDeleteAsync(context.RequestAborted);
        await db.Set<Question>().Where(question => EF.Property<Guid>(question, "QuizId") == quizId)
            .ExecuteDeleteAsync(context.RequestAborted);
        db.ChangeTracker.Clear();
        db.Attach(quiz);
        db.Entry(quiz).Property(item => item.Title).IsModified = true;
        foreach (var question in quiz.Questions)
        {
            db.Entry(question).State = EntityState.Added;
            foreach (var option in question.Options) db.Entry(option).State = EntityState.Added;
        }
        await db.SaveChangesAsync(context.RequestAborted);
        await transaction.CommitAsync(context.RequestAborted);
        return Results.NoContent();
    }

    private static string? Validate(string title, IReadOnlyList<CreateQuestionRequest?>? questions)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Quiz title is required.";
        if (title.Trim().Length > 200) return "Quiz title cannot exceed 200 characters.";
        if (questions is null || questions.Count == 0) return "Quiz questions are required.";
        foreach (var question in questions)
        {
            if (question is null) return "Quiz questions cannot contain null items.";
            if (string.IsNullOrWhiteSpace(question.Text) || question.Text.Trim().Length > 4000)
                return "Question text is required and cannot exceed 4000 characters.";
            if (question.Options is null || question.Options.Count < 2)
                return "Every question must have at least two options.";
            if (question.Options.Any(option => option is null))
                return "Question options cannot contain null items.";
            if (question.Options.Any(option => string.IsNullOrWhiteSpace(option!.Text) || option.Text.Trim().Length > 2000))
                return "Option text is required and cannot exceed 2000 characters.";
        }
        return null;
    }

    private static QuizDetailResponse ToResponse(Quiz quiz) => new(
        quiz.Id, quiz.Title, quiz.IsLocked,
        quiz.Questions.OrderBy(question => question.Order).Select(question => new TeacherQuizQuestionResponse(
            question.Id, question.Text, question.Order,
            question.Options.OrderBy(option => option.Order).Select(option => new TeacherQuizOptionResponse(
                option.Id, option.Text, option.Order, option.IsCorrect)).ToList())).ToList());

    private static IResult Failure(HttpContext context, string code, string detail, ErrorType type) =>
        Result.Failure(new Error(code, detail, type)).ToHttpResult(context);
}

public sealed record CreateQuizRequest(string Title, IReadOnlyList<CreateQuestionRequest?> Questions);
public sealed record CreateQuestionRequest(string Text, int Order, IReadOnlyList<CreateOptionRequest?> Options);
public sealed record CreateOptionRequest(string Text, int Order, bool IsCorrect);
public sealed record CreateQuizResponse(Guid Id, string Title);
public sealed record QuizSummaryResponse(Guid Id, string Title, bool IsLocked, bool IsAssigned);
public sealed record UpdateQuizRequest(string Title, IReadOnlyList<CreateQuestionRequest?> Questions);
public sealed record QuizDetailResponse(Guid Id, string Title, bool IsLocked, IReadOnlyList<TeacherQuizQuestionResponse> Questions);
public sealed record TeacherQuizQuestionResponse(Guid Id, string Text, int Order, IReadOnlyList<TeacherQuizOptionResponse> Options);
public sealed record TeacherQuizOptionResponse(Guid Id, string Text, int Order, bool IsCorrect);
