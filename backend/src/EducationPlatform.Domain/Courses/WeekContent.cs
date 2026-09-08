namespace EducationPlatform.Domain.Courses;

public sealed class WeekContent
{
    private WeekContent()
    {
    }

    private WeekContent(Guid id, string title, int order, WeekContentType type)
    {
        EnsureIdentifier(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        EnsurePositiveOrder(order);

        Id = id;
        Title = title.Trim();
        Order = order;
        Type = type;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public int Order { get; private set; }
    public WeekContentType Type { get; private set; }
    public string? TopicText { get; private set; }
    public string? VideoUrl { get; private set; }
    public string? VideoDescription { get; private set; }
    public Guid? QuizId { get; private set; }
    public bool IsActive { get; private set; }

    public static WeekContent Topic(Guid id, string title, int order, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new WeekContent(id, title, order, WeekContentType.Topic)
        {
            TopicText = text.Trim()
        };
    }

    public static WeekContent Video(
        Guid id,
        string title,
        int order,
        string videoUrl,
        string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoUrl);
        if (!Uri.TryCreate(videoUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Video URL must be an absolute HTTP or HTTPS URL.", nameof(videoUrl));
        }

        return new WeekContent(id, title, order, WeekContentType.Video)
        {
            VideoUrl = uri.AbsoluteUri,
            VideoDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
        };
    }

    public static WeekContent Quiz(Guid id, string title, int order, Guid quizId)
    {
        EnsureIdentifier(quizId, nameof(quizId));
        return new WeekContent(id, title, order, WeekContentType.Quiz) { QuizId = quizId };
    }

    internal void ChangeOrder(int order)
    {
        EnsurePositiveOrder(order);
        Order = order;
    }

    internal void UpdateTopic(string title, string text)
    {
        EnsureType(WeekContentType.Topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Title = title.Trim();
        TopicText = text.Trim();
    }

    internal void UpdateVideo(string title, string videoUrl, string? description)
    {
        EnsureType(WeekContentType.Video);
        var updated = Video(Id, title, Order, videoUrl, description);
        Title = updated.Title;
        VideoUrl = updated.VideoUrl;
        VideoDescription = updated.VideoDescription;
    }

    internal void Deactivate() => IsActive = false;

    private void EnsureType(WeekContentType expected)
    {
        if (Type != expected)
        {
            throw new InvalidOperationException($"Only {expected} content can be updated with this operation.");
        }
    }

    private static void EnsureIdentifier(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier cannot be empty.", name);
        }
    }

    private static void EnsurePositiveOrder(int order)
    {
        if (order <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(order), "Order must be positive.");
        }
    }
}
