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

    [Fact]
    public void UnlockedQuiz_CanBeUpdatedAtomically()
    {
        var quiz = new Quiz(Guid.NewGuid(), "Old", Guid.NewGuid());
        var oldQuestion = quiz.AddQuestion(Guid.NewGuid(), "Old question", 1);
        quiz.AddOption(oldQuestion.Id, Guid.NewGuid(), "Correct", 1, true);
        quiz.AddOption(oldQuestion.Id, Guid.NewGuid(), "Wrong", 2, false);

        quiz.Update(" Updated ",
        [
            new QuizQuestionDefinition("New question", 1,
            [
                new QuizOptionDefinition("No", 1, false),
                new QuizOptionDefinition("Yes", 2, true)
            ])
        ]);

        Assert.Equal("Updated", quiz.Title);
        var question = Assert.Single(quiz.Questions);
        Assert.Equal("New question", question.Text);
        Assert.True(question.IsValid);
    }

    [Fact]
    public void InvalidUpdate_DoesNotChangeExistingQuiz()
    {
        var quiz = new Quiz(Guid.NewGuid(), "Original", Guid.NewGuid());
        var originalQuestion = quiz.AddQuestion(Guid.NewGuid(), "Original question", 1);
        quiz.AddOption(originalQuestion.Id, Guid.NewGuid(), "Correct", 1, true);
        quiz.AddOption(originalQuestion.Id, Guid.NewGuid(), "Wrong", 2, false);

        Assert.Throws<InvalidOperationException>(() => quiz.Update("Changed",
        [
            new QuizQuestionDefinition("Invalid", 1,
            [new QuizOptionDefinition("Only", 1, true)])
        ]));

        Assert.Equal("Original", quiz.Title);
        Assert.Equal(originalQuestion.Id, Assert.Single(quiz.Questions).Id);
    }

    [Fact]
    public void LockedQuiz_DoesNotAllowUpdate()
    {
        var quiz = new Quiz(Guid.NewGuid(), "Quiz", Guid.NewGuid());
        quiz.Lock();

        Assert.Throws<InvalidOperationException>(() => quiz.Update("Changed",
        [
            new QuizQuestionDefinition("Question", 1,
            [
                new QuizOptionDefinition("Correct", 1, true),
                new QuizOptionDefinition("Wrong", 2, false)
            ])
        ]));
    }
}
