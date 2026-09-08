using EducationPlatform.Domain.Courses;

namespace EducationPlatform.Domain.Tests.Courses;

public sealed class CourseTests
{
    [Fact]
    public void NewCourse_StartsAsDraft()
    {
        var course = CreateCourse();
        Assert.Equal(CourseStatus.Draft, course.Status);
        Assert.Empty(course.Weeks);
    }

    [Fact]
    public void DraftCourse_CanContainEmptyWeek_ButCannotBePublished()
    {
        var course = CreateCourse();
        course.AddWeek(Guid.NewGuid(), "Week 1", 1);
        Assert.Throws<InvalidOperationException>(course.Publish);
    }

    [Fact]
    public void ValidCourse_CanBePublished()
    {
        var course = CreateCourse();
        var week = course.AddWeek(Guid.NewGuid(), "Week 1", 1);
        course.AddTopic(week.Id, Guid.NewGuid(), "Algorithms", 1, "Topic text");
        course.Publish();
        Assert.Equal(CourseStatus.Published, course.Status);
    }

    [Fact]
    public void PublishedCourse_RequiresNewWeekToBeAddedAtomicallyWithContent()
    {
        var course = CreateCourse();
        var firstWeek = course.AddWeek(Guid.NewGuid(), "Week 1", 1);
        course.AddTopic(firstWeek.Id, Guid.NewGuid(), "Topic", 1, "Text");
        course.Publish();

        Assert.Throws<InvalidOperationException>(() =>
            course.AddWeek(Guid.NewGuid(), "Empty week", 2));

        var newWeek = course.AddWeekWithTopic(
            Guid.NewGuid(), "Week 2", 2,
            Guid.NewGuid(), "Topic 2", 1, "Text 2");
        Assert.Single(newWeek.Contents);
    }

    [Fact]
    public void PublishedWeek_LastActiveContentCannotBeDeactivated()
    {
        var course = CreateCourse();
        var week = course.AddWeek(Guid.NewGuid(), "Week 1", 1);
        var content = course.AddTopic(week.Id, Guid.NewGuid(), "Topic", 1, "Text");
        course.Publish();

        Assert.Throws<InvalidOperationException>(() =>
            course.DeactivateContent(week.Id, content.Id));
        Assert.True(content.IsActive);
    }

    [Fact]
    public void WeekAndContentOrders_MustBeUnique()
    {
        var course = CreateCourse();
        var week = course.AddWeek(Guid.NewGuid(), "Week 1", 1);
        Assert.Throws<InvalidOperationException>(() => course.AddWeek(Guid.NewGuid(), "Week 2", 1));
        course.AddTopic(week.Id, Guid.NewGuid(), "Topic", 1, "Text");
        Assert.Throws<InvalidOperationException>(() =>
            course.AddVideo(week.Id, Guid.NewGuid(), "Video", 1, "https://example.com/video", null));
    }

    [Fact]
    public void PublishedTopicAndVideo_CanBeUpdated()
    {
        var course = CreateCourse();
        var week = course.AddWeek(Guid.NewGuid(), "Week 1", 1);
        var topic = course.AddTopic(week.Id, Guid.NewGuid(), "Topic", 1, "Old");
        var video = course.AddVideo(week.Id, Guid.NewGuid(), "Video", 2, "https://example.com/old", null);
        course.Publish();
        course.UpdateTopic(week.Id, topic.Id, "Updated topic", "New");
        course.UpdateVideo(week.Id, video.Id, "Updated video", "https://example.com/new", "Description");
        Assert.Equal("New", topic.TopicText);
        Assert.Equal("https://example.com/new", video.VideoUrl);
    }

    [Fact]
    public void Reorder_RequiresEveryItemExactlyOnce()
    {
        var course = CreateCourse();
        var first = course.AddWeek(Guid.NewGuid(), "Week 1", 1);
        var second = course.AddWeek(Guid.NewGuid(), "Week 2", 2);
        Assert.Throws<InvalidOperationException>(() => course.ReorderWeeks([first.Id]));
        course.ReorderWeeks([second.Id, first.Id]);
        Assert.Equal(1, second.Order);
        Assert.Equal(2, first.Order);
    }

    [Fact]
    public void Quiz_CannotBeAssignedTwiceWithinCourse()
    {
        var course = CreateCourse();
        var week = course.AddWeek(Guid.NewGuid(), "Week 1", 1);
        var quizId = Guid.NewGuid();
        course.AddQuiz(week.Id, Guid.NewGuid(), "Quiz", 1, quizId);
        Assert.Throws<InvalidOperationException>(() =>
            course.AddQuiz(week.Id, Guid.NewGuid(), "Same quiz", 2, quizId));
    }

    private static Course CreateCourse() =>
        new(Guid.NewGuid(), "Information Technologies", "Description", Guid.NewGuid());
}
