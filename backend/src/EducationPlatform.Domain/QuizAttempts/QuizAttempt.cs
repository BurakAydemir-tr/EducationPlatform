namespace EducationPlatform.Domain.QuizAttempts;

public sealed class QuizAttempt
{
    private readonly List<QuizAttemptAnswer> _answers = [];

    private QuizAttempt()
    {
    }

    public QuizAttempt(Guid id, Guid studentId, Guid quizId, DateTimeOffset startedAt)
    {
        EnsureIdentifier(id, nameof(id));
        EnsureIdentifier(studentId, nameof(studentId));
        EnsureIdentifier(quizId, nameof(quizId));

        Id = id;
        StudentId = studentId;
        QuizId = quizId;
        StartedAt = startedAt;
    }

    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid QuizId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int CorrectAnswerCount { get; private set; }
    public int QuestionCount { get; private set; }
    public decimal Score { get; private set; }
    public IReadOnlyCollection<QuizAttemptAnswer> Answers => _answers.AsReadOnly();
    public bool IsCompleted => CompletedAt.HasValue;

    public void Complete(
        IReadOnlyCollection<QuizAttemptAnswerSelection> answers,
        int correctAnswerCount,
        int questionCount,
        decimal score,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(answers);
        if (IsCompleted) throw new InvalidOperationException("Quiz attempt is already completed.");
        if (questionCount <= 0) throw new ArgumentOutOfRangeException(nameof(questionCount));
        if (answers.Count != questionCount || answers.Select(answer => answer.QuestionId).Distinct().Count() != answers.Count)
            throw new ArgumentException("Every question must have exactly one answer.", nameof(answers));
        if (correctAnswerCount < 0 || correctAnswerCount > questionCount)
            throw new ArgumentOutOfRangeException(nameof(correctAnswerCount));
        if (score < 0 || score > 100) throw new ArgumentOutOfRangeException(nameof(score));
        if (completedAt < StartedAt) throw new ArgumentOutOfRangeException(nameof(completedAt));

        foreach (var answer in answers)
        {
            _answers.Add(new QuizAttemptAnswer(Guid.NewGuid(), answer.QuestionId, answer.SelectedOptionId));
        }

        CorrectAnswerCount = correctAnswerCount;
        QuestionCount = questionCount;
        Score = score;
        CompletedAt = completedAt;
    }

    private static void EnsureIdentifier(Guid value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException("Identifier cannot be empty.", name);
    }
}

public sealed class QuizAttemptAnswer
{
    private QuizAttemptAnswer()
    {
    }

    internal QuizAttemptAnswer(Guid id, Guid questionId, Guid selectedOptionId)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identifier cannot be empty.", nameof(id));
        if (questionId == Guid.Empty) throw new ArgumentException("Identifier cannot be empty.", nameof(questionId));
        if (selectedOptionId == Guid.Empty) throw new ArgumentException("Identifier cannot be empty.", nameof(selectedOptionId));
        Id = id;
        QuestionId = questionId;
        SelectedOptionId = selectedOptionId;
    }

    public Guid Id { get; private set; }
    public Guid QuestionId { get; private set; }
    public Guid SelectedOptionId { get; private set; }
}

public sealed record QuizAttemptAnswerSelection(Guid QuestionId, Guid SelectedOptionId);
