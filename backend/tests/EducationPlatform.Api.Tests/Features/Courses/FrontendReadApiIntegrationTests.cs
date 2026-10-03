using System.Net;
using System.Net.Http.Json;
using EducationPlatform.Api.Features.Courses;
using EducationPlatform.Api.Features.Learning;
using EducationPlatform.Api.Features.Quizzes;
using EducationPlatform.Api.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EducationPlatform.Api.Tests.Features.Courses;

public sealed partial class CourseApiIntegrationTests
{
    [Fact]
    public async Task FrontendRead_CourseAssignmentsAndHistoricalStudentsCanBeRediscovered()
    {
        using var teacher = await AuthenticatedClient("course-teacher");
        using var otherTeacher = await AuthenticatedClient("other-teacher");
        using var student = await AuthenticatedClient("course-student");
        using var anonymous = _factory!.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var studentId = await StudentId();
        var firstRoom = await CreateRoomAndAddStudent(teacher, studentId, "Read A");
        var secondRoom = await CreateRoomAndAddStudent(teacher, studentId, "Read B");
        var course = await CreatePublishedLearningCourse(teacher, [firstRoom, secondRoom]);
        var classroomsPath = $"/api/courses/{course.CourseId}/classrooms";
        var studentsPath = $"/api/courses/{course.CourseId}/students";

        using var freshTeacher = FreshReadClient(teacher);
        var assignments = await freshTeacher.GetFromJsonAsync<List<CourseClassroomResponse>>(classroomsPath);
        Assert.Equal([firstRoom, secondRoom], assignments!.Select(item => item.ClassroomId));
        Assert.Equal(["Read A", "Read B"], assignments!.Select(item => item.Name));
        var students = await freshTeacher.GetFromJsonAsync<List<CourseStudentResponse>>(studentsPath);
        var discoveredStudent = Assert.Single(students!);
        Assert.Equal(studentId, discoveredStudent.StudentId);
        Assert.Equal("course-student", discoveredStudent.UserName);
        Assert.DoesNotContain(students!, item => item.UserName == "other-student");

        Assert.Equal(HttpStatusCode.NoContent, (await teacher.DeleteAsync($"/api/courses/{course.CourseId}/classrooms/{firstRoom}")).StatusCode);
        assignments = await freshTeacher.GetFromJsonAsync<List<CourseClassroomResponse>>(classroomsPath);
        Assert.Equal(secondRoom, Assert.Single(assignments!).ClassroomId);

        Assert.Equal(HttpStatusCode.NoContent, (await teacher.DeleteAsync($"/api/classrooms/{secondRoom}/students/{studentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.PostAsJsonAsync($"/api/classrooms/{secondRoom}/students", new EducationPlatform.Api.Features.Classrooms.AddStudentRequest("course-student"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.DeleteAsync($"/api/classrooms/{secondRoom}/students/{studentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.DeleteAsync($"/api/courses/{course.CourseId}/classrooms/{secondRoom}")).StatusCode);

        Assert.Empty((await freshTeacher.GetFromJsonAsync<List<CourseClassroomResponse>>(classroomsPath))!);
        students = await freshTeacher.GetFromJsonAsync<List<CourseStudentResponse>>(studentsPath);
        Assert.Equal(studentId, Assert.Single(students!).StudentId);
        Assert.Equal(HttpStatusCode.OK, (await freshTeacher.GetAsync($"/api/courses/{course.CourseId}/students/{studentId}/progress")).StatusCode);

        foreach (var path in new[] { classroomsPath, studentsPath })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await otherTeacher.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await freshTeacher.GetAsync(path.Replace(course.CourseId.ToString(), Guid.NewGuid().ToString()))).StatusCode);
        }
    }

    [Fact]
    public async Task FrontendRead_TeacherCanDiscoverOwnQuizzesWithoutAnswerDisclosure()
    {
        using var teacher = await AuthenticatedClient("course-teacher");
        using var otherTeacher = await AuthenticatedClient("other-teacher");
        using var student = await AuthenticatedClient("course-student");
        using var anonymous = _factory!.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        static CreateQuizRequest QuizRequest(string title) => new(title,
            [new CreateQuestionRequest("Question", 1,
                [new CreateOptionRequest("Correct", 1, true), new CreateOptionRequest("Wrong", 2, false)])]);
        var unassignedResponse = await teacher.PostAsJsonAsync("/api/quizzes", QuizRequest("Available quiz"));
        Assert.Equal(HttpStatusCode.Created, unassignedResponse.StatusCode);
        var unassigned = (await unassignedResponse.Content.ReadFromJsonAsync<CreateQuizResponse>())!;
        var foreignResponse = await otherTeacher.PostAsJsonAsync("/api/quizzes", QuizRequest("Foreign quiz"));
        Assert.Equal(HttpStatusCode.Created, foreignResponse.StatusCode);
        var foreign = (await foreignResponse.Content.ReadFromJsonAsync<CreateQuizResponse>())!;

        var room = await CreateRoomAndAddStudent(teacher, await StudentId(), "Quiz discovery");
        var course = await CreatePublishedLearningCourse(teacher, [room]);
        var attempt = await StartAttempt(student, course.QuizId);
        Assert.NotEmpty(attempt.Questions);
        var detail = await teacher.GetFromJsonAsync<CourseDetailResponse>($"/api/courses/{course.CourseId}");
        var weekId = Assert.Single(detail!.Weeks).Id;
        Assert.Equal(HttpStatusCode.NoContent,
            (await teacher.DeleteAsync($"/api/courses/{course.CourseId}/weeks/{weekId}/contents/{course.QuizContentId}")).StatusCode);

        using var freshTeacher = FreshReadClient(teacher);
        var listResponse = await freshTeacher.GetAsync("/api/quizzes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var json = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("questions", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isCorrect", json, StringComparison.OrdinalIgnoreCase);
        var summaries = (await listResponse.Content.ReadFromJsonAsync<List<QuizSummaryResponse>>())!;
        Assert.Equal(summaries.OrderBy(item => item.Title).ThenBy(item => item.Id).Select(item => item.Id), summaries.Select(item => item.Id));
        var available = Assert.Single(summaries, item => item.Id == unassigned.Id);
        Assert.False(available.IsLocked);
        Assert.False(available.IsAssigned);
        var used = Assert.Single(summaries, item => item.Id == course.QuizId);
        Assert.True(used.IsLocked);
        Assert.True(used.IsAssigned); // An inactive WeekContent still reserves its Quiz.
        Assert.DoesNotContain(summaries, item => item.Id == foreign.Id);

        using var freshOtherTeacher = FreshReadClient(otherTeacher);
        Assert.Equal(foreign.Id, Assert.Single((await freshOtherTeacher.GetFromJsonAsync<List<QuizSummaryResponse>>("/api/quizzes"))!).Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/quizzes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/quizzes")).StatusCode);
    }

    [Fact]
    public async Task FrontendRead_ProgressIncludesActiveTopicVideoAndQuizCompletions()
    {
        using var teacher = await AuthenticatedClient("course-teacher");
        using var otherTeacher = await AuthenticatedClient("other-teacher");
        using var student = await AuthenticatedClient("course-student");
        using var otherStudent = await AuthenticatedClient("other-student");
        var studentId = await StudentId();
        var room = await CreateRoomAndAddStudent(teacher, studentId, "Progress discovery");
        var course = await CreatePublishedLearningCourse(teacher, [room]);
        var detail = await teacher.GetFromJsonAsync<CourseDetailResponse>($"/api/courses/{course.CourseId}");
        var weekId = Assert.Single(detail!.Weeks).Id;
        var videoResponse = await teacher.PostAsJsonAsync($"/api/courses/{course.CourseId}/weeks/{weekId}/contents/video",
            new AddVideoRequest("Video", 3, "https://example.com/video", null));
        Assert.Equal(HttpStatusCode.Created, videoResponse.StatusCode);
        var video = (await videoResponse.Content.ReadFromJsonAsync<WeekContentResponse>())!;

        Assert.Equal(HttpStatusCode.NoContent, (await student.PostAsync($"/api/student/contents/{course.TopicId}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await student.PostAsync($"/api/student/contents/{video.Id}/complete", null)).StatusCode);
        var attempt = await StartAttempt(student, course.QuizId);
        var question = Assert.Single(attempt.Questions);
        var completedAttempt = await student.PostAsJsonAsync($"/api/student/quiz-attempts/{attempt.AttemptId}/complete",
            new CompleteQuizAttemptRequest([new QuizAnswerRequest(question.Id, question.Options.Single(item => item.Order == 1).Id)]));
        Assert.Equal(HttpStatusCode.OK, completedAttempt.StatusCode);

        using var freshStudent = FreshReadClient(student);
        var courseDetail = await freshStudent.GetFromJsonAsync<CourseDetailResponse>($"/api/student/courses/{course.CourseId}");
        var progress = await freshStudent.GetFromJsonAsync<CourseProgressResponse>($"/api/student/courses/{course.CourseId}/progress");
        Assert.Equal(3, courseDetail!.Weeks.Single().Contents.Count);
        Assert.Equal(3, progress!.CompletedContentCount);
        Assert.Equal(100m, progress.Percentage);
        Assert.Equal([course.TopicId, course.QuizContentId, video.Id], progress.CompletedContents.Select(item => item.ContentId));
        Assert.All(progress.CompletedContents, item => Assert.NotEqual(default, item.CompletedAt));
        using var freshTeacher = FreshReadClient(teacher);
        var teacherProgress = await freshTeacher.GetFromJsonAsync<CourseProgressResponse>($"/api/courses/{course.CourseId}/students/{studentId}/progress");
        Assert.Equal(progress.CompletedContents, teacherProgress!.CompletedContents);

        Assert.Equal(HttpStatusCode.NoContent,
            (await teacher.DeleteAsync($"/api/courses/{course.CourseId}/weeks/{weekId}/contents/{video.Id}")).StatusCode);
        progress = await freshStudent.GetFromJsonAsync<CourseProgressResponse>($"/api/student/courses/{course.CourseId}/progress");
        Assert.Equal(2, progress!.TotalContentCount);
        Assert.Equal(2, progress.CompletedContentCount);
        Assert.DoesNotContain(progress.CompletedContents, item => item.ContentId == video.Id);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>();
            Assert.True(await db.ContentProgress.AnyAsync(item => item.StudentId == studentId && item.ContentId == video.Id));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await otherStudent.GetAsync($"/api/student/courses/{course.CourseId}/progress")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherTeacher.GetAsync($"/api/courses/{course.CourseId}/students/{studentId}/progress")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/courses/{course.CourseId}/students/{studentId}/progress")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.DeleteAsync($"/api/classrooms/{room}/students/{studentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await freshStudent.GetAsync($"/api/student/courses/{course.CourseId}/progress")).StatusCode);
        Assert.Equal(2, (await freshTeacher.GetFromJsonAsync<CourseProgressResponse>($"/api/courses/{course.CourseId}/students/{studentId}/progress"))!.CompletedContents.Count);
    }

    private HttpClient FreshReadClient(HttpClient authenticatedClient)
    {
        var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = authenticatedClient.DefaultRequestHeaders.Authorization;
        return client;
    }
}
