using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Features.Auth;
using EducationPlatform.Api.Features.Classrooms;
using EducationPlatform.Api.Features.Courses;
using EducationPlatform.Api.Features.Quizzes;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Api.Persistence.Courses;
using EducationPlatform.Api.Persistence.Identity;
using EducationPlatform.Domain.Courses;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EducationPlatform.Api.Tests.Features.Courses;

public sealed class CourseApiIntegrationTests : IAsyncLifetime
{
    private const string ConnectionStringVariable = "EducationPlatformTests__ConnectionString";
    private const string DatabasePrefix = "education_platform_tests_";
    private const string Password = "Integration123!";
    private WebApplicationFactory<Program>? _factory;
    private string? _adminConnectionString;
    private string? _databaseName;

    [Fact]
    public async Task Teacher_CanPreparePublishAndAssignCourse_StudentAccessFollowsActiveRelationships()
    {
        using var teacher = await AuthenticatedClient("course-teacher");
        using var otherTeacher = await AuthenticatedClient("other-teacher");
        using var student = await AuthenticatedClient("course-student");

        var studentForbidden = await student.PostAsJsonAsync("/api/courses", new CreateCourseRequest("Forbidden", null));
        Assert.Equal(HttpStatusCode.Forbidden, studentForbidden.StatusCode);
        await AssertProblemCode(studentForbidden, "forbidden");

        var roomResponse = await teacher.PostAsJsonAsync("/api/classrooms", new CreateClassroomRequest("7-A"));
        var room = await roomResponse.Content.ReadFromJsonAsync<CreateClassroomResponse>();
        Assert.Equal(HttpStatusCode.Created, roomResponse.StatusCode);

        var secondRoomResponse = await teacher.PostAsJsonAsync("/api/classrooms", new CreateClassroomRequest("7-B"));
        var secondRoom = await secondRoomResponse.Content.ReadFromJsonAsync<CreateClassroomResponse>();
        Assert.Equal(HttpStatusCode.Created, secondRoomResponse.StatusCode);

        var otherRoomResponse = await otherTeacher.PostAsJsonAsync("/api/classrooms", new CreateClassroomRequest("Other"));
        var otherRoom = await otherRoomResponse.Content.ReadFromJsonAsync<CreateClassroomResponse>();

        var addStudent = await teacher.PostAsJsonAsync($"/api/classrooms/{room!.Id}/students", new AddStudentRequest("course-student"));
        Assert.Equal(HttpStatusCode.NoContent, addStudent.StatusCode);

        var courseResponse = await teacher.PostAsJsonAsync("/api/courses", new CreateCourseRequest("Bilişim", "Temel ders"));
        var course = await courseResponse.Content.ReadFromJsonAsync<CreateCourseResponse>();
        Assert.Equal(HttpStatusCode.Created, courseResponse.StatusCode);
        Assert.Equal(CourseStatus.Draft, course!.Status);

        var draftList = await student.GetFromJsonAsync<List<CourseSummaryResponse>>("/api/student/courses");
        Assert.Empty(draftList!);

        var emptyPublish = await teacher.PostAsync($"/api/courses/{course.Id}/publish", null);
        Assert.Equal(HttpStatusCode.Conflict, emptyPublish.StatusCode);
        await AssertProblemCode(emptyPublish, "course_not_publishable");

        var weekResponse = await teacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks", new AddWeekRequest("Hafta 1", 1));
        var week = await weekResponse.Content.ReadFromJsonAsync<WeekSummaryResponse>();
        Assert.Equal(HttpStatusCode.Created, weekResponse.StatusCode);

        var topicResponse = await teacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks/{week!.Id}/contents/topic", new AddTopicRequest("Algoritma", 1, "Eski metin"));
        var topic = await topicResponse.Content.ReadFromJsonAsync<WeekContentResponse>();
        Assert.Equal(HttpStatusCode.Created, topicResponse.StatusCode);

        var videoResponse = await teacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks/{week.Id}/contents/video", new AddVideoRequest("Video", 2, "https://example.com/video", null));
        var video = await videoResponse.Content.ReadFromJsonAsync<WeekContentResponse>();
        Assert.Equal(HttpStatusCode.Created, videoResponse.StatusCode);

        var quizResponse = await teacher.PostAsJsonAsync("/api/quizzes", new CreateQuizRequest("Mini quiz",
        [
            new CreateQuestionRequest("Soru?", 1,
            [
                new CreateOptionRequest("Doğru", 1, true),
                new CreateOptionRequest("Yanlış", 2, false)
            ])
        ]));
        var quiz = await quizResponse.Content.ReadFromJsonAsync<CreateQuizResponse>();
        Assert.Equal(HttpStatusCode.Created, quizResponse.StatusCode);

        var quizContent = await teacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks/{week.Id}/contents/quiz", new AddQuizContentRequest("Test", 3, quiz!.Id));
        var quizWeekContent = await quizContent.Content.ReadFromJsonAsync<WeekContentResponse>();
        Assert.Equal(HttpStatusCode.Created, quizContent.StatusCode);

        var reorder = await teacher.PutAsJsonAsync(
            $"/api/courses/{course.Id}/weeks/{week.Id}/contents/order",
            new ReorderRequest([quizWeekContent!.Id, topic!.Id, video!.Id]));
        Assert.True(
            reorder.StatusCode == HttpStatusCode.NoContent,
            $"Unexpected reorder response: {reorder.StatusCode} {await reorder.Content.ReadAsStringAsync()}");
        var nullReorder = await teacher.PutAsJsonAsync(
            $"/api/courses/{course.Id}/weeks/{week.Id}/contents/order",
            new { ids = (object?)null });
        Assert.Equal(HttpStatusCode.BadRequest, nullReorder.StatusCode);
        await AssertProblemCode(nullReorder, "invalid_order");

        var forbidden = await otherTeacher.PostAsJsonAsync($"/api/courses/{course.Id}/weeks", new AddWeekRequest("Forbidden", 2));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await AssertProblemCode(forbidden, "forbidden");

        var assign = await teacher.PostAsync($"/api/courses/{course.Id}/classrooms/{room.Id}", null);
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
        var secondAssign = await teacher.PostAsync($"/api/courses/{course.Id}/classrooms/{secondRoom!.Id}", null);
        Assert.Equal(HttpStatusCode.NoContent, secondAssign.StatusCode);
        var foreignAssign = await teacher.PostAsync($"/api/courses/{course.Id}/classrooms/{otherRoom!.Id}", null);
        Assert.Equal(HttpStatusCode.Forbidden, foreignAssign.StatusCode);
        await AssertProblemCode(foreignAssign, "forbidden");
        var duplicateAssign = await teacher.PostAsync($"/api/courses/{course.Id}/classrooms/{room.Id}", null);
        Assert.Equal(HttpStatusCode.Conflict, duplicateAssign.StatusCode);
        await AssertProblemCode(duplicateAssign, "duplicate_course_assignment");

        var publish = await teacher.PostAsync($"/api/courses/{course.Id}/publish", null);
        Assert.Equal(HttpStatusCode.NoContent, publish.StatusCode);

        var emptyPublishedWeek = await teacher.PostAsJsonAsync(
            $"/api/courses/{course.Id}/weeks",
            new AddWeekRequest("Empty published week", 2));
        Assert.Equal(HttpStatusCode.BadRequest, emptyPublishedWeek.StatusCode);
        await AssertProblemCode(emptyPublishedWeek, "invalid_operation");

        var atomicWeekResponse = await teacher.PostAsJsonAsync(
            $"/api/courses/{course.Id}/weeks/with-topic",
            new AddPublishedWeekWithTopicRequest("Hafta 2", 2, "Yeni konu", "İçerik"));
        Assert.Equal(HttpStatusCode.Created, atomicWeekResponse.StatusCode);
        var atomicWeek = await atomicWeekResponse.Content.ReadFromJsonAsync<WeekSummaryResponse>();
        Assert.NotNull(atomicWeek);

        var studentCourses = await student.GetFromJsonAsync<List<CourseSummaryResponse>>("/api/student/courses");
        Assert.Single(studentCourses!, item => item.Id == course.Id);
        var detail = await student.GetFromJsonAsync<CourseDetailResponse>($"/api/student/courses/{course.Id}");
        Assert.Equal(2, detail!.Weeks.Count);
        Assert.Equal(3, detail.Weeks.Single(item => item.Id == week.Id).Contents.Count);
        Assert.Equal([quizWeekContent.Id, topic.Id, video.Id], detail.Weeks.Single(item => item.Id == week.Id).Contents.Select(item => item.Id));
        var atomicContent = detail.Weeks.Single(item => item.Id == atomicWeek.Id).Contents.Single();
        var deactivateLast = await teacher.DeleteAsync($"/api/courses/{course.Id}/weeks/{atomicWeek.Id}/contents/{atomicContent.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, deactivateLast.StatusCode);
        await AssertProblemCode(deactivateLast, "invalid_operation");

        var update = await teacher.PutAsJsonAsync($"/api/courses/{course.Id}/weeks/{week.Id}/contents/{topic!.Id}/topic", new UpdateTopicRequest("Algoritma güncel", "Yeni metin"));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        detail = await student.GetFromJsonAsync<CourseDetailResponse>($"/api/student/courses/{course.Id}");
        Assert.Equal("Yeni metin", detail!.Weeks.Single(item => item.Id == week.Id).Contents.Single(item => item.Id == topic.Id).TopicText);

        var deactivate = await teacher.DeleteAsync($"/api/courses/{course.Id}/weeks/{week.Id}/contents/{video.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        detail = await student.GetFromJsonAsync<CourseDetailResponse>($"/api/student/courses/{course.Id}");
        Assert.DoesNotContain(detail!.Weeks.Single(item => item.Id == week.Id).Contents, item => item.Id == video.Id);

        var remove = await teacher.DeleteAsync($"/api/courses/{course.Id}/classrooms/{room.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var inaccessible = await student.GetAsync($"/api/student/courses/{course.Id}");
        Assert.Equal(HttpStatusCode.NotFound, inaccessible.StatusCode);
        await AssertProblemCode(inaccessible, "course_not_accessible");

        var reassign = await teacher.PostAsync($"/api/courses/{course.Id}/classrooms/{room.Id}", null);
        Assert.Equal(HttpStatusCode.NoContent, reassign.StatusCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>();
        var assignments = await db.CourseClassroomAssignments.Where(item => item.CourseId == course.Id && item.ClassroomId == room.Id).ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.Single(assignments, item => item.RemovedAt is null);
        Assert.Single(assignments, item => item.RemovedAt is not null);

        var studentId = await db.Users.Where(user => user.UserName == "course-student").Select(user => user.Id).SingleAsync();
        var removeMembership = await teacher.DeleteAsync($"/api/classrooms/{room.Id}/students/{studentId}");
        Assert.Equal(HttpStatusCode.NoContent, removeMembership.StatusCode);
        var inaccessibleAfterMembershipRemoval = await student.GetAsync($"/api/student/courses/{course.Id}");
        Assert.Equal(HttpStatusCode.NotFound, inaccessibleAfterMembershipRemoval.StatusCode);
        await AssertProblemCode(inaccessibleAfterMembershipRemoval, "course_not_accessible");
    }

    [Fact]
    public async Task ConcurrentQuizAssignment_ReturnsOneCreatedAndOneExpectedConflict()
    {
        using var teacher = await AuthenticatedClient("course-teacher");
        var quizResponse = await teacher.PostAsJsonAsync("/api/quizzes", new CreateQuizRequest("Concurrent quiz",
        [
            new CreateQuestionRequest("Question", 1,
            [
                new CreateOptionRequest("Correct", 1, true),
                new CreateOptionRequest("Wrong", 2, false)
            ])
        ]));
        var quiz = await quizResponse.Content.ReadFromJsonAsync<CreateQuizResponse>();

        async Task<(Guid CourseId, Guid WeekId)> CreateCourseWithWeek(string suffix)
        {
            var courseResponse = await teacher.PostAsJsonAsync("/api/courses", new CreateCourseRequest($"Course {suffix}", null));
            var course = await courseResponse.Content.ReadFromJsonAsync<CreateCourseResponse>();
            var weekResponse = await teacher.PostAsJsonAsync($"/api/courses/{course!.Id}/weeks", new AddWeekRequest("Week", 1));
            var week = await weekResponse.Content.ReadFromJsonAsync<WeekSummaryResponse>();
            return (course.Id, week!.Id);
        }

        var first = await CreateCourseWithWeek("one");
        var second = await CreateCourseWithWeek("two");
        var responses = await Task.WhenAll(
            teacher.PostAsJsonAsync($"/api/courses/{first.CourseId}/weeks/{first.WeekId}/contents/quiz", new AddQuizContentRequest("Quiz", 1, quiz!.Id)),
            teacher.PostAsJsonAsync($"/api/courses/{second.CourseId}/weeks/{second.WeekId}/contents/quiz", new AddQuizContentRequest("Quiz", 1, quiz.Id)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertProblemCode(conflict, "quiz_already_assigned");
    }

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException($"Set {ConnectionStringVariable} before running integration tests.");
        _adminConnectionString = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres", Pooling = false, Timeout = 5, CommandTimeout = 30 }.ConnectionString;
        _databaseName = $"{DatabasePrefix}{Guid.NewGuid():N}";
        var testConnection = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _databaseName, Pooling = false }.ConnectionString;
        await AdminCommand($"CREATE DATABASE \"{_databaseName}\"");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
            });
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = testConnection,
                ["Jwt:Issuer"] = "integration-tests",
                ["Jwt:Audience"] = "integration-tests",
                ["Jwt:SigningKey"] = "integration-test-signing-key-at-least-32-bytes"
            }));
        });
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>();
        await db.Database.MigrateAsync();
        await Seed(scope.ServiceProvider);
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) { await _factory.DisposeAsync(); NpgsqlConnection.ClearAllPools(); }
        if (_adminConnectionString is null || _databaseName is null || !_databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal)) return;
        await AdminCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    private async Task<HttpClient> AuthenticatedClient(string userName)
    {
        var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, Password));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static async Task AssertProblemCode(HttpResponseMessage response, string code)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(code, problem!.Extensions["code"]?.ToString());
    }

    private static async Task Seed(IServiceProvider services)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var (name, role) in new[] { ("course-teacher", RoleNames.Teacher), ("other-teacher", RoleNames.Teacher), ("course-student", RoleNames.Student) })
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = name, Name = name };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }
    }

    private async Task AdminCommand(string sql)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        await command.ExecuteNonQueryAsync();
    }
}
