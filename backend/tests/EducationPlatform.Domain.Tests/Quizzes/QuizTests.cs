using EducationPlatform.Domain.Quizzes;

namespace EducationPlatform.Domain.Tests.Quizzes;

public sealed class QuizTests
{
    [Fact]
    public void Quiz_IsValidOnlyWithQuestionAndExactlyOneCorrectOption()
    {
        var quiz = new Quiz(Guid.NewGuid(), "Quiz", Guid.NewGuid());
        var question = quiz.AddQuestion(Guid.NewGuid(), "Question", 1);
        quiz.AddOption(question.Id, Guid.NewGuid(), "Correct", 1, true);
        Assert.False(quiz.IsValid);
        quiz.AddOption(question.Id, Guid.NewGuid(), "Wrong", 2, false);
        Assert.True(quiz.IsValid);
        Assert.Throws<InvalidOperationException>(() =>
            quiz.AddOption(question.Id, Guid.NewGuid(), "Another correct", 3, true));
    }

    [Fact]
    public void LockedQuiz_DoesNotAllowNewQuestion()
    {
        var quiz = new Quiz(Guid.NewGuid(), "Quiz", Guid.NewGuid());
        quiz.Lock();
        Assert.Throws<InvalidOperationException>(() =>
            quiz.AddQuestion(Guid.NewGuid(), "Question", 1));
    }
}
