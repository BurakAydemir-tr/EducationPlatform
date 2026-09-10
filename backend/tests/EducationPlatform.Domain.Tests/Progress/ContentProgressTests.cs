using EducationPlatform.Domain.Progress;

namespace EducationPlatform.Domain.Tests.Progress;

public sealed class ContentProgressTests
{
    [Fact]
    public void Constructor_CreatesCompletionRecord()
    {
        var studentId = Guid.NewGuid();
        var contentId = Guid.NewGuid();
        var completedAt = DateTimeOffset.UtcNow;

        var progress = new ContentProgress(studentId, contentId, completedAt);

        Assert.Equal(studentId, progress.StudentId);
        Assert.Equal(contentId, progress.ContentId);
        Assert.Equal(completedAt, progress.CompletedAt);
    }

    [Fact]
    public void Constructor_RejectsEmptyIdentifiers()
    {
        Assert.Throws<ArgumentException>(() => new ContentProgress(Guid.Empty, Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new ContentProgress(Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow));
    }
}
