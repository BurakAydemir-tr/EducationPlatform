using EducationPlatform.Domain.QuizAttempts;

namespace EducationPlatform.Domain.Tests.QuizAttempts;

public sealed class QuizAttemptTests
{
    [Fact]
    public void NewAttempt_StartsIncomplete()
    {
        var attempt = CreateAttempt();

        Assert.False(attempt.IsCompleted);
        Assert.Null(attempt.CompletedAt);
        Assert.Empty(attempt.Answers);
    }

    [Fact]
    public void Complete_SetsResultAndAnswers()
    {
        var attempt = CreateAttempt();
        var completedAt = attempt.StartedAt.AddMinutes(1);
        var answers = new[]
        {
            new QuizAttemptAnswerSelection(Guid.NewGuid(), Guid.NewGuid()),
            new QuizAttemptAnswerSelection(Guid.NewGuid(), Guid.NewGuid())
        };

        attempt.Complete(answers, 1, 2, 50m, completedAt);

        Assert.True(attempt.IsCompleted);
        Assert.Equal(completedAt, attempt.CompletedAt);
        Assert.Equal(1, attempt.CorrectAnswerCount);
        Assert.Equal(2, attempt.QuestionCount);
        Assert.Equal(50m, attempt.Score);
        Assert.Equal(2, attempt.Answers.Count);
    }

    [Fact]
    public void Complete_RejectsDuplicateOrMissingAnswers()
    {
        var attempt = CreateAttempt();
        var questionId = Guid.NewGuid();
        var answers = new[]
        {
            new QuizAttemptAnswerSelection(questionId, Guid.NewGuid()),
            new QuizAttemptAnswerSelection(questionId, Guid.NewGuid())
        };

        Assert.Throws<ArgumentException>(() => attempt.Complete(answers, 1, 2, 50m, attempt.StartedAt));
    }

    [Fact]
    public void CompletedAttempt_CannotBeCompletedAgain()
    {
        var attempt = CreateAttempt();
        var answers = new[] { new QuizAttemptAnswerSelection(Guid.NewGuid(), Guid.NewGuid()) };
        attempt.Complete(answers, 1, 1, 100m, attempt.StartedAt);

        Assert.Throws<InvalidOperationException>(() => attempt.Complete(answers, 1, 1, 100m, attempt.StartedAt));
    }

    private static QuizAttempt CreateAttempt() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
}
