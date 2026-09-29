using System.Net;
using System.Net.Http.Json;
using EducationPlatform.Api.Features.Courses;
using EducationPlatform.Api.Features.Quizzes;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EducationPlatform.Api.Tests.Features.Courses;

public sealed partial class CourseApiIntegrationTests
{
    [Fact]
    public async Task StructuralConcurrency_DeactivateCannotRemoveBothPublishedContents()
    {
        using var teacher = await AuthenticatedClient("course-teacher");
        var course = await StructuralCourse(teacher, contentCount: 2);
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.PostAsync($"/api/courses/{course.Id}/publish", null)).StatusCode);
        var week = Assert.Single(course.Weeks);
        var contents = week.Contents.OrderBy(item => item.Order).ToArray();
        var responses = await RunStructuralRace(course.Id,
            () => teacher.DeleteAsync($"/api/courses/{course.Id}/weeks/{week.Id}/contents/{contents[0].Id}"),
            () => teacher.DeleteAsync($"/api/courses/{course.Id}/weeks/{week.Id}/contents/{contents[1].Id}"));
        Assert.Equal(HttpStatusCode.NoContent, responses.First.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, responses.Second.StatusCode);
        await AssertProblemCode(responses.Second, "invalid_operation");
        var stored = await StoredStructuralCourse(course.Id);
        Assert.Equal(CourseStatus.Published, stored.Status);
        Assert.Single(Assert.Single(stored.Weeks).Contents, item => item.IsActive);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task StructuralConcurrency_PublishValidatesFreshGraph(bool publishFirst, bool addWeek)
    {
        using var teacher = await AuthenticatedClient("course-teacher");
        var course = await StructuralCourse(teacher);
        var week = Assert.Single(course.Weeks);
        Task<HttpResponseMessage> Publish() => teacher.PostAsync($"/api/courses/{course.Id}/publish", null);
        Task<HttpResponseMessage> Mutate() => addWeek
            ? teacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks", new AddWeekRequest("Empty week", 2))
            : teacher.DeleteAsync($"/api/courses/{course.Id}/weeks/{week.Id}/contents/{Assert.Single(week.Contents).Id}");
        var responses = await RunStructuralRace(course.Id, publishFirst ? Publish : Mutate, publishFirst ? Mutate : Publish);
        Assert.Equal(publishFirst || !addWeek ? HttpStatusCode.NoContent : HttpStatusCode.Created, responses.First.StatusCode);
        Assert.Equal(publishFirst ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict, responses.Second.StatusCode);
        await AssertProblemCode(responses.Second, publishFirst ? "invalid_operation" : "course_not_publishable");
        var stored = await StoredStructuralCourse(course.Id);
        Assert.Equal(publishFirst ? CourseStatus.Published : CourseStatus.Draft, stored.Status);
        if (publishFirst) Assert.All(stored.Weeks, item => Assert.Contains(item.Contents, content => content.IsActive));
        else if (addWeek) Assert.Equal(2, stored.Weeks.Count);
        else Assert.DoesNotContain(Assert.Single(stored.Weeks).Contents, item => item.IsActive);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task StructuralConcurrency_ReorderUsesCurrentIdSetAndNeverCommitsNegativeOrders(bool reorderFirst, bool weeks)
    {
        using var teacher = await AuthenticatedClient("course-teacher");
        var course = await StructuralCourse(teacher, weeks ? 2 : 1, weeks ? 1 : 2);
        var week = course.Weeks.OrderBy(item => item.Order).First();
        var originalIds = weeks
            ? course.Weeks.OrderBy(item => item.Order).Select(item => item.Id).ToArray()
            : week.Contents.OrderBy(item => item.Order).Select(item => item.Id).ToArray();
        var reorderPath = weeks ? $"/api/courses/{course.Id}/weeks/order" : $"/api/courses/{course.Id}/weeks/{week.Id}/contents/order";
        Task<HttpResponseMessage> Reorder() => teacher.PutAsJsonAsync(reorderPath, new ReorderRequest(originalIds.Reverse().ToArray()));
        Task<HttpResponseMessage> Add() => weeks
            ? teacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks", new AddWeekRequest("Third week", 3))
            : teacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks/{week.Id}/contents/topic", new AddTopicRequest("Third topic", 3, "Text"));
        var responses = await RunStructuralRace(course.Id, reorderFirst ? Reorder : Add, reorderFirst ? Add : Reorder);
        Assert.Equal(reorderFirst ? HttpStatusCode.NoContent : HttpStatusCode.Created, responses.First.StatusCode);
        Assert.Equal(reorderFirst ? HttpStatusCode.Created : HttpStatusCode.BadRequest, responses.Second.StatusCode);
        if (!reorderFirst) await AssertProblemCode(responses.Second, "invalid_order");
        var stored = await StoredStructuralCourse(course.Id);
        var ordered = weeks
            ? stored.Weeks.OrderBy(item => item.Order).Select(item => (item.Id, item.Order)).ToArray()
            : Assert.Single(stored.Weeks).Contents.OrderBy(item => item.Order).Select(item => (item.Id, item.Order)).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, ordered.Select(item => item.Order));
        Assert.Equal(reorderFirst ? originalIds.Reverse() : originalIds, ordered.Take(2).Select(item => item.Id));
    }

    [Theory]
    [InlineData("week")]
    [InlineData("topic")]
    [InlineData("video")]
    [InlineData("quiz")]
    [InlineData("with-topic")]
    [InlineData("with-video")]
    [InlineData("with-quiz")]
    public async Task StructuralConcurrency_SameOrderAddReturnsControlledFailure(string kind)
    {
        using var teacher = await AuthenticatedClient("course-teacher");
        var course = await StructuralCourse(teacher);
        var week = Assert.Single(course.Weeks);
        if (kind.StartsWith("with-", StringComparison.Ordinal))
            Assert.Equal(HttpStatusCode.NoContent, (await teacher.PostAsync($"/api/courses/{course.Id}/publish", null)).StatusCode);
        async Task<Guid> CreateQuiz()
        {
            var response = await teacher.PostAsJsonAsync("/api/quizzes", new CreateQuizRequest("Quiz",
                [new CreateQuestionRequest("Question", 1, [new CreateOptionRequest("Yes", 1, true), new CreateOptionRequest("No", 2, false)])]));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<CreateQuizResponse>())!.Id;
        }
        var firstQuiz = kind.Contains("quiz", StringComparison.Ordinal) ? await CreateQuiz() : Guid.Empty;
        var secondQuiz = kind.Contains("quiz", StringComparison.Ordinal) ? await CreateQuiz() : Guid.Empty;
        Task<HttpResponseMessage> Add(Guid quizId) => kind switch
        {
            "week" => teacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks", new AddWeekRequest("Week", 2)),
            "topic" => teacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks/{week.Id}/contents/topic", new AddTopicRequest("Topic", 2, "Text")),
            "video" => teacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks/{week.Id}/contents/video", new AddVideoRequest("Video", 2, "https://example.com/video", null)),
            "quiz" => teacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks/{week.Id}/contents/quiz", new AddQuizContentRequest("Quiz", 2, quizId)),
            "with-topic" => teacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks/with-topic", new AddPublishedWeekWithTopicRequest("Week", 2, "Topic", "Text")),
            "with-video" => teacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks/with-video", new AddPublishedWeekWithVideoRequest("Week", 2, "Video", "https://example.com/video", null)),
            "with-quiz" => teacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks/with-quiz", new AddPublishedWeekWithQuizRequest("Week", 2, "Quiz", quizId)),
            _ => throw new InvalidOperationException("Unknown test case.")
        };
        var responses = await RunStructuralRace(course.Id, () => Add(firstQuiz), () => Add(secondQuiz));
        Assert.Equal(HttpStatusCode.Created, responses.First.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, responses.Second.StatusCode);
        await AssertProblemCode(responses.Second, "invalid_operation");
        var stored = await StoredStructuralCourse(course.Id);
        if (kind == "week" || kind.StartsWith("with-", StringComparison.Ordinal))
            Assert.Equal(new[] { 1, 2 }, stored.Weeks.OrderBy(item => item.Order).Select(item => item.Order));
        else Assert.Equal(new[] { 1, 2 }, Assert.Single(stored.Weeks).Contents.OrderBy(item => item.Order).Select(item => item.Order));
        if (stored.Status == CourseStatus.Published)
            Assert.All(stored.Weeks, item => Assert.Contains(item.Contents, content => content.IsActive));
    }

    [Fact]
    public async Task StructuralConcurrency_ConcurrentPublishOnlyPublishesOnce()
    {
        using var teacher = await AuthenticatedClient("course-teacher");
        var course = await StructuralCourse(teacher);
        Task<HttpResponseMessage> Publish() => teacher.PostAsync($"/api/courses/{course.Id}/publish", null);
        var responses = await RunStructuralRace(course.Id, Publish, Publish);
        Assert.Equal(HttpStatusCode.NoContent, responses.First.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, responses.Second.StatusCode);
        await AssertProblemCode(responses.Second, "course_not_publishable");
        Assert.Equal(CourseStatus.Published, (await StoredStructuralCourse(course.Id)).Status);
    }

    private async Task<Course> StructuralCourse(HttpClient teacher, int weekCount = 1, int contentCount = 1)
    {
        var response = await teacher.PostAsJsonAsync("/api/courses", new CreateCourseRequest("Structural concurrency", null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var courseId = (await response.Content.ReadFromJsonAsync<CreateCourseResponse>())!.Id;
        for (var index = 1; index <= weekCount; index++)
        {
            var weekResponse = await teacher.PostAsJsonAsync($"/api/courses/{courseId}/weeks", new AddWeekRequest($"Week {index}", index));
            Assert.Equal(HttpStatusCode.Created, weekResponse.StatusCode);
            var weekId = (await weekResponse.Content.ReadFromJsonAsync<WeekSummaryResponse>())!.Id;
            for (var order = 1; order <= contentCount; order++)
                Assert.Equal(HttpStatusCode.Created, (await teacher.PostAsJsonAsync($"/api/courses/{courseId}/weeks/{weekId}/contents/topic", new AddTopicRequest($"Topic {order}", order, "Text"))).StatusCode);
        }
        return await StoredStructuralCourse(courseId);
    }

    private async Task<Course> StoredStructuralCourse(Guid courseId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>()
            .Courses.AsNoTracking().Include(item => item.Weeks).ThenInclude(item => item.Contents)
            .SingleAsync(item => item.Id == courseId);
    }

    private async Task<(HttpResponseMessage First, HttpResponseMessage Second)> RunStructuralRace(
        Guid courseId, Func<Task<HttpResponseMessage>> first, Func<Task<HttpResponseMessage>> second)
    {
        await using var blocker = new NpgsqlConnection(_testConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await LockCourseAsync(blocker, transaction, courseId);
        Task<HttpResponseMessage>? firstTask = null;
        Task<HttpResponseMessage>? secondTask = null;
        var released = false;
        try
        {
            firstTask = first();
            var firstPid = await WaitForStructuralWaiter(blocker.ProcessID);
            secondTask = second();
            // PostgreSQL reports the second waiter behind the first queued request.
            // Verify that queue dependency, rather than assuming scheduling from Task.WhenAll.
            await WaitForStructuralWaiter(firstPid);
            await transaction.CommitAsync();
            released = true;
            return (await firstTask.WaitAsync(TimeSpan.FromSeconds(15)), await secondTask.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            if (!released) await transaction.RollbackAsync();
            if (firstTask is not null) await firstTask.WaitAsync(TimeSpan.FromSeconds(15));
            if (secondTask is not null) await secondTask.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    private async Task<int> WaitForStructuralWaiter(int blockerPid)
    {
        await using var connection = new NpgsqlConnection(_testConnectionString);
        await connection.OpenAsync();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand("""
                SELECT pid FROM pg_stat_activity
                WHERE datname = current_database()
                  AND wait_event_type = 'Lock'
                  AND query LIKE '%"Courses"%'
                  AND query LIKE '%FOR UPDATE%'
                  AND @blocker = ANY(pg_blocking_pids(pid))
                """, connection);
            command.Parameters.AddWithValue("blocker", blockerPid);
            if (await command.ExecuteScalarAsync() is int pid) return pid;
            await Task.Delay(25);
        }
        throw new TimeoutException("Expected Course lock dependency was not observed.");
    }
}
