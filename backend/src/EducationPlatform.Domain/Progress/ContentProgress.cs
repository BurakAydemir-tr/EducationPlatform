namespace EducationPlatform.Domain.Progress;

public sealed class ContentProgress
{
    private ContentProgress()
    {
    }

    public ContentProgress(Guid studentId, Guid contentId, DateTimeOffset completedAt)
    {
        if (studentId == Guid.Empty) throw new ArgumentException("Identifier cannot be empty.", nameof(studentId));
        if (contentId == Guid.Empty) throw new ArgumentException("Identifier cannot be empty.", nameof(contentId));
        StudentId = studentId;
        ContentId = contentId;
        CompletedAt = completedAt;
    }

    public Guid StudentId { get; private set; }
    public Guid ContentId { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }
}
