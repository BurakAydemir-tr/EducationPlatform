using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Common.Results;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Domain.Quizzes;

namespace EducationPlatform.Api.Features.Quizzes;

public static class QuizEndpoints
{
    public static IEndpointRouteBuilder MapQuizEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/quizzes", CreateAsync)
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Teacher));
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateQuizRequest request, HttpContext context, EducationPlatformDbContext db)
    {
        if (!CurrentUser.TryGetId(context.User, out var teacherId))
            return Failure(context, "authentication_required", "Authentication is required.", ErrorType.Authentication);
        if (request.Questions is null || request.Questions.Count == 0
            || request.Questions.Any(question => question.Options is null))
            return Failure(context, "invalid_quiz", "Quiz questions and options are required.", ErrorType.Validation);
        Quiz quiz;
        try
        {
            quiz = new Quiz(Guid.NewGuid(), request.Title, teacherId);
            foreach (var questionRequest in request.Questions.OrderBy(question => question.Order))
            {
                var question = quiz.AddQuestion(Guid.NewGuid(), questionRequest.Text, questionRequest.Order);
                foreach (var option in questionRequest.Options.OrderBy(item => item.Order))
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

    private static IResult Failure(HttpContext context, string code, string detail, ErrorType type) =>
        Result.Failure(new Error(code, detail, type)).ToHttpResult(context);
}

public sealed record CreateQuizRequest(string Title, IReadOnlyList<CreateQuestionRequest> Questions);
public sealed record CreateQuestionRequest(string Text, int Order, IReadOnlyList<CreateOptionRequest> Options);
public sealed record CreateOptionRequest(string Text, int Order, bool IsCorrect);
public sealed record CreateQuizResponse(Guid Id, string Title);
