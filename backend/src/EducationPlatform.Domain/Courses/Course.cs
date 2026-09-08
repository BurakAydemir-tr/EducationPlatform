namespace EducationPlatform.Domain.Courses;

public sealed class Course
{
    private readonly List<CourseWeek> _weeks = [];

    private Course()
    {
    }

    public Course(Guid id, string title, string? description, Guid teacherId)
    {
        EnsureIdentifier(id, nameof(id));
        EnsureIdentifier(teacherId, nameof(teacherId));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Id = id;
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        TeacherId = teacherId;
        Status = CourseStatus.Draft;
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid TeacherId { get; private set; }
    public CourseStatus Status { get; private set; }
    public IReadOnlyCollection<CourseWeek> Weeks => _weeks.AsReadOnly();

    public CourseWeek AddWeek(Guid id, string title, int order)
    {
        if (Status == CourseStatus.Published)
            throw new InvalidOperationException("A published course week must be added with content.");
        var week = CreateWeek(id, title, order);
        _weeks.Add(week);
        return week;
    }

    public CourseWeek AddWeekWithTopic(
        Guid weekId, string weekTitle, int weekOrder,
        Guid contentId, string contentTitle, int contentOrder, string text)
    {
        EnsurePublished();
        var week = CreateWeek(weekId, weekTitle, weekOrder);
        week.AddContent(WeekContent.Topic(contentId, contentTitle, contentOrder, text));
        _weeks.Add(week);
        return week;
    }

    public CourseWeek AddWeekWithVideo(
        Guid weekId, string weekTitle, int weekOrder,
        Guid contentId, string contentTitle, int contentOrder, string videoUrl, string? description)
    {
        EnsurePublished();
        var week = CreateWeek(weekId, weekTitle, weekOrder);
        week.AddContent(WeekContent.Video(contentId, contentTitle, contentOrder, videoUrl, description));
        _weeks.Add(week);
        return week;
    }

    public CourseWeek AddWeekWithQuiz(
        Guid weekId, string weekTitle, int weekOrder,
        Guid contentId, string contentTitle, int contentOrder, Guid quizId)
    {
        EnsurePublished();
        if (_weeks.SelectMany(week => week.Contents).Any(content => content.QuizId == quizId))
            throw new InvalidOperationException("A quiz can be assigned to only one content.");
        var week = CreateWeek(weekId, weekTitle, weekOrder);
        week.AddContent(WeekContent.Quiz(contentId, contentTitle, contentOrder, quizId));
        _weeks.Add(week);
        return week;
    }

    public WeekContent AddTopic(Guid weekId, Guid contentId, string title, int order, string text)
    {
        var content = WeekContent.Topic(contentId, title, order, text);
        GetWeek(weekId).AddContent(content);
        return content;
    }

    public WeekContent AddVideo(
        Guid weekId,
        Guid contentId,
        string title,
        int order,
        string videoUrl,
        string? description)
    {
        var content = WeekContent.Video(contentId, title, order, videoUrl, description);
        GetWeek(weekId).AddContent(content);
        return content;
    }

    public WeekContent AddQuiz(Guid weekId, Guid contentId, string title, int order, Guid quizId)
    {
        if (_weeks.SelectMany(week => week.Contents).Any(content => content.QuizId == quizId))
        {
            throw new InvalidOperationException("A quiz can be assigned to only one content.");
        }

        var content = WeekContent.Quiz(contentId, title, order, quizId);
        GetWeek(weekId).AddContent(content);
        return content;
    }

    public void ReorderWeeks(IReadOnlyList<Guid> orderedWeekIds)
    {
        ArgumentNullException.ThrowIfNull(orderedWeekIds);
        if (orderedWeekIds.Count != _weeks.Count
            || orderedWeekIds.Distinct().Count() != orderedWeekIds.Count
            || orderedWeekIds.Any(id => _weeks.All(week => week.Id != id)))
        {
            throw new InvalidOperationException("Week order must contain each week exactly once.");
        }

        for (var index = 0; index < orderedWeekIds.Count; index++)
        {
            _weeks.Single(week => week.Id == orderedWeekIds[index]).ChangeOrder(index + 1);
        }
    }

    public void ReorderContents(Guid weekId, IReadOnlyList<Guid> orderedContentIds) =>
        GetWeek(weekId).Reorder(orderedContentIds);

    public void UpdateTopic(Guid weekId, Guid contentId, string title, string text) =>
        GetWeek(weekId).GetContent(contentId).UpdateTopic(title, text);

    public void UpdateVideo(Guid weekId, Guid contentId, string title, string url, string? description) =>
        GetWeek(weekId).GetContent(contentId).UpdateVideo(title, url, description);

    public void DeactivateContent(Guid weekId, Guid contentId)
    {
        var week = GetWeek(weekId);
        var content = week.GetContent(contentId);
        if (Status == CourseStatus.Published
            && content.IsActive
            && week.Contents.Count(item => item.IsActive) == 1)
        {
            throw new InvalidOperationException("The last active content of a published week cannot be deactivated.");
        }
        content.Deactivate();
    }

    public void Publish()
    {
        if (Status == CourseStatus.Published)
        {
            throw new InvalidOperationException("Course is already published.");
        }

        if (_weeks.Count == 0 || _weeks.Any(week => !week.Contents.Any(content => content.IsActive)))
        {
            throw new InvalidOperationException("Course must have at least one week and each week must contain active content.");
        }

        Status = CourseStatus.Published;
    }

    private CourseWeek GetWeek(Guid weekId) =>
        _weeks.SingleOrDefault(week => week.Id == weekId)
        ?? throw new InvalidOperationException("Week does not belong to this course.");

    private CourseWeek CreateWeek(Guid id, string title, int order)
    {
        if (_weeks.Any(week => week.Order == order))
            throw new InvalidOperationException("Week order must be unique within a course.");
        return new CourseWeek(id, title, order);
    }

    private void EnsurePublished()
    {
        if (Status != CourseStatus.Published)
            throw new InvalidOperationException("This operation requires a published course.");
    }

    private static void EnsureIdentifier(Guid value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException("Identifier cannot be empty.", name);
    }
}
