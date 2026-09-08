namespace EducationPlatform.Domain.Courses;

public sealed class CourseWeek
{
    private readonly List<WeekContent> _contents = [];

    private CourseWeek()
    {
    }

    internal CourseWeek(Guid id, string title, int order)
    {
        EnsureIdentifier(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        EnsurePositiveOrder(order);
        Id = id;
        Title = title.Trim();
        Order = order;
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public int Order { get; private set; }
    public IReadOnlyCollection<WeekContent> Contents => _contents.AsReadOnly();

    internal void AddContent(WeekContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (_contents.Any(item => item.IsActive && item.Order == content.Order))
        {
            throw new InvalidOperationException("Content order must be unique within a week.");
        }

        _contents.Add(content);
    }

    internal WeekContent GetContent(Guid contentId) =>
        _contents.SingleOrDefault(item => item.Id == contentId)
        ?? throw new InvalidOperationException("Content does not belong to this week.");

    internal void Reorder(IReadOnlyList<Guid> orderedContentIds)
    {
        ArgumentNullException.ThrowIfNull(orderedContentIds);
        var active = _contents.Where(item => item.IsActive).ToList();
        if (orderedContentIds.Count != active.Count
            || orderedContentIds.Distinct().Count() != orderedContentIds.Count
            || orderedContentIds.Any(id => active.All(item => item.Id != id)))
        {
            throw new InvalidOperationException("Content order must contain each active content exactly once.");
        }

        for (var index = 0; index < orderedContentIds.Count; index++)
        {
            active.Single(item => item.Id == orderedContentIds[index]).ChangeOrder(index + 1);
        }
    }

    internal void ChangeOrder(int order)
    {
        EnsurePositiveOrder(order);
        Order = order;
    }

    private static void EnsureIdentifier(Guid value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException("Identifier cannot be empty.", name);
    }

    private static void EnsurePositiveOrder(int order)
    {
        if (order <= 0) throw new ArgumentOutOfRangeException(nameof(order), "Order must be positive.");
    }
}
