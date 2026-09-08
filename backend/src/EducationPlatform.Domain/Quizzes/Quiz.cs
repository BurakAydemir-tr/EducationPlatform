namespace EducationPlatform.Domain.Quizzes;

public sealed class Quiz
{
    private readonly List<Question> _questions = [];

    private Quiz() { }

    public Quiz(Guid id, string title, Guid teacherId)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identifier cannot be empty.", nameof(id));
        if (teacherId == Guid.Empty) throw new ArgumentException("Identifier cannot be empty.", nameof(teacherId));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Id = id;
        Title = title.Trim();
        TeacherId = teacherId;
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public Guid TeacherId { get; private set; }
    public bool IsLocked { get; private set; }
    public IReadOnlyCollection<Question> Questions => _questions.AsReadOnly();

    public Question AddQuestion(Guid id, string text, int order)
    {
        EnsureUnlocked();
        if (order <= 0) throw new ArgumentOutOfRangeException(nameof(order));
        if (_questions.Any(question => question.Order == order))
            throw new InvalidOperationException("Question order must be unique.");
        var question = new Question(id, text, order);
        _questions.Add(question);
        return question;
    }

    public Option AddOption(Guid questionId, Guid id, string text, int order, bool isCorrect)
    {
        EnsureUnlocked();
        var question = _questions.SingleOrDefault(item => item.Id == questionId)
            ?? throw new InvalidOperationException("Question does not belong to this quiz.");
        return question.AddOption(id, text, order, isCorrect);
    }

    public bool IsValid => _questions.Count > 0 && _questions.All(question => question.IsValid);

    public void Lock() => IsLocked = true;

    private void EnsureUnlocked()
    {
        if (IsLocked) throw new InvalidOperationException("Quiz is locked.");
    }
}

public sealed class Question
{
    private readonly List<Option> _options = [];
    private Question() { }

    internal Question(Guid id, string text, int order)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identifier cannot be empty.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Id = id;
        Text = text.Trim();
        Order = order;
    }

    public Guid Id { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public int Order { get; private set; }
    public IReadOnlyCollection<Option> Options => _options.AsReadOnly();
    public bool IsValid => _options.Count >= 2 && _options.Count(option => option.IsCorrect) == 1;

    internal Option AddOption(Guid id, string text, int order, bool isCorrect)
    {
        if (order <= 0) throw new ArgumentOutOfRangeException(nameof(order));
        if (_options.Any(option => option.Order == order))
            throw new InvalidOperationException("Option order must be unique.");
        if (isCorrect && _options.Any(option => option.IsCorrect))
            throw new InvalidOperationException("A question can have only one correct option.");
        var option = new Option(id, text, order, isCorrect);
        _options.Add(option);
        return option;
    }
}

public sealed class Option
{
    private Option() { }

    internal Option(Guid id, string text, int order, bool isCorrect)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identifier cannot be empty.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Id = id;
        Text = text.Trim();
        Order = order;
        IsCorrect = isCorrect;
    }

    public Guid Id { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public int Order { get; private set; }
    public bool IsCorrect { get; private set; }
}
