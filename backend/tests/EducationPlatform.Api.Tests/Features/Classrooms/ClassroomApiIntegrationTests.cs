using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Features.Auth;
using EducationPlatform.Api.Features.Classrooms;
using EducationPlatform.Api.Features.Students;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Api.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace EducationPlatform.Api.Tests.Features.Classrooms;

public sealed class ClassroomApiIntegrationTests : IAsyncLifetime
{
    private const string ConnectionStringVariable = "EducationPlatformTests__ConnectionString";
    private const string DatabasePrefix = "education_platform_tests_";
    private const string Password = "Integration123!";
    private WebApplicationFactory<Program>? _factory;
    private string? _adminConnectionString;
    private string? _databaseName;

    [Fact]
    public async Task Teacher_CanCreateClassroomAddAndRemoveStudent_WithAuthorizationRules()
    {
        using var teacherClient = await CreateAuthenticatedClient("teacher-one");
        var createStudentResponse = await teacherClient.PostAsJsonAsync(
            "/api/students",
            new CreateStudentRequest("student-created", "Student Created", Password, "STU-NEW"));
        Assert.Equal(HttpStatusCode.Created, createStudentResponse.StatusCode);
        var createdStudent = await createStudentResponse.Content.ReadFromJsonAsync<CreateStudentResponse>();
        Assert.NotNull(createdStudent);

        var duplicateStudentResponse = await teacherClient.PostAsJsonAsync(
            "/api/students",
            new CreateStudentRequest("student-created", "Another Student", Password, "STU-OTHER"));
        Assert.Equal(HttpStatusCode.Conflict, duplicateStudentResponse.StatusCode);
        await AssertProblemCodeAsync(duplicateStudentResponse, "duplicate_student_account");

        var duplicateStudentCodeResponse = await teacherClient.PostAsJsonAsync(
            "/api/students",
            new CreateStudentRequest("student-with-duplicate-code", "Duplicate Code", Password, "stu-new"));
        Assert.Equal(HttpStatusCode.Conflict, duplicateStudentCodeResponse.StatusCode);
        await AssertProblemCodeAsync(duplicateStudentCodeResponse, "duplicate_student_account");

        var userNameCollidesWithCodeResponse = await teacherClient.PostAsJsonAsync(
            "/api/students",
            new CreateStudentRequest("STU-NEW", "Cross Collision", Password, null));
        Assert.Equal(HttpStatusCode.Conflict, userNameCollidesWithCodeResponse.StatusCode);
        await AssertProblemCodeAsync(userNameCollidesWithCodeResponse, "duplicate_student_account");

        var codeCollidesWithUserNameResponse = await teacherClient.PostAsJsonAsync(
            "/api/students",
            new CreateStudentRequest("student-other", "Cross Collision", Password, "STUDENT-CREATED"));
        Assert.Equal(HttpStatusCode.Conflict, codeCollidesWithUserNameResponse.StatusCode);
        await AssertProblemCodeAsync(codeCollidesWithUserNameResponse, "duplicate_student_account");

        var concurrentRequests = await Task.WhenAll(
            teacherClient.PostAsJsonAsync(
                "/api/students",
                new CreateStudentRequest("student-concurrent", "Concurrent One", Password, "CONCURRENT-1")),
            teacherClient.PostAsJsonAsync(
                "/api/students",
                new CreateStudentRequest("student-concurrent", "Concurrent Two", Password, "CONCURRENT-2")));
        Assert.Single(concurrentRequests, response => response.StatusCode == HttpStatusCode.Created);
        var concurrentConflict = Assert.Single(
            concurrentRequests,
            response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertProblemCodeAsync(concurrentConflict, "duplicate_student_account");

        var createResponse = await teacherClient.PostAsJsonAsync(
            "/api/classrooms",
            new CreateClassroomRequest("  6-A  "));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var classroom = await createResponse.Content.ReadFromJsonAsync<CreateClassroomResponse>();
        Assert.NotNull(classroom);
        Assert.Equal("6-A", classroom.Name);
        Assert.Equal($"/api/classrooms/{classroom.Id}", createResponse.Headers.Location?.ToString());

        var addResponse = await teacherClient.PostAsJsonAsync(
            $"/api/classrooms/{classroom.Id}/students",
            new AddStudentRequest("student-created"));
        Assert.Equal(HttpStatusCode.NoContent, addResponse.StatusCode);

        var duplicateResponse = await teacherClient.PostAsJsonAsync(
            $"/api/classrooms/{classroom.Id}/students",
            new AddStudentRequest("STU-NEW"));
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        await AssertProblemCodeAsync(duplicateResponse, "duplicate_membership");

        var teacherAsStudentResponse = await teacherClient.PostAsJsonAsync(
            $"/api/classrooms/{classroom.Id}/students",
            new AddStudentRequest("teacher-two"));
        Assert.Equal(HttpStatusCode.BadRequest, teacherAsStudentResponse.StatusCode);
        await AssertProblemCodeAsync(teacherAsStudentResponse, "user_is_not_student");

        using var otherTeacherClient = await CreateAuthenticatedClient("teacher-two");
        var forbiddenResponse = await otherTeacherClient.PostAsJsonAsync(
            $"/api/classrooms/{classroom.Id}/students",
            new AddStudentRequest("student-created"));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        await AssertProblemCodeAsync(forbiddenResponse, "forbidden");

        using var studentClient = await CreateAuthenticatedClient("student-one");
        var roleForbiddenResponse = await studentClient.PostAsJsonAsync(
            "/api/classrooms",
            new CreateClassroomRequest("Not allowed"));
        Assert.Equal(HttpStatusCode.Forbidden, roleForbiddenResponse.StatusCode);
        await AssertProblemCodeAsync(roleForbiddenResponse, "forbidden");

        var studentId = createdStudent.Id;
        var forbiddenRemoveResponse = await otherTeacherClient.DeleteAsync(
            $"/api/classrooms/{classroom.Id}/students/{studentId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRemoveResponse.StatusCode);
        await AssertProblemCodeAsync(forbiddenRemoveResponse, "forbidden");

        var removeResponse = await teacherClient.DeleteAsync(
            $"/api/classrooms/{classroom.Id}/students/{studentId}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var removeAgainResponse = await teacherClient.DeleteAsync(
            $"/api/classrooms/{classroom.Id}/students/{studentId}");
        Assert.Equal(HttpStatusCode.NotFound, removeAgainResponse.StatusCode);
        await AssertProblemCodeAsync(removeAgainResponse, "membership_not_found");

        var readdResponse = await teacherClient.PostAsJsonAsync(
            $"/api/classrooms/{classroom.Id}/students",
            new AddStudentRequest("student-created"));
        Assert.Equal(HttpStatusCode.NoContent, readdResponse.StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>();
        var memberships = await dbContext.ClassroomMemberships
            .Where(item => item.ClassroomId == classroom.Id && item.StudentId == studentId)
            .ToListAsync();
        Assert.Equal(2, memberships.Count);
        Assert.Single(memberships, membership => membership.LeftAt is not null);
        Assert.Single(memberships, membership => membership.LeftAt is null);

        await AssertRefreshTokenRotationAsync();
    }

    public async Task InitializeAsync()
    {
        var configuredConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringVariable} to a PostgreSQL connection string before running integration tests.");
        }

        _adminConnectionString = new NpgsqlConnectionStringBuilder(configuredConnectionString)
        {
            Database = "postgres",
            Pooling = false,
            Timeout = 5,
            CommandTimeout = 30
        }.ConnectionString;

        _databaseName = $"{DatabasePrefix}{Guid.NewGuid():N}";
        var testConnection = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = _databaseName,
            Pooling = false
        }.ConnectionString;

        await ExecuteAdminCommandAsync($"CREATE DATABASE \"{_databaseName}\"");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = testConnection,
                    ["Jwt:Issuer"] = "integration-tests",
                    ["Jwt:Audience"] = "integration-tests",
                    ["Jwt:SigningKey"] = "integration-test-signing-key-at-least-32-bytes"
                }));
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>();
        await dbContext.Database.MigrateAsync();
        await SeedUsersAsync(scope.ServiceProvider);
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
            NpgsqlConnection.ClearAllPools();
        }

        if (_adminConnectionString is null
            || _databaseName is null
            || !_databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal))
        {
            return;
        }

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)",
            connection)
        {
            CommandTimeout = 30
        };
        await drop.ExecuteNonQueryAsync();
    }

    private async Task<HttpClient> CreateAuthenticatedClient(string userName)
    {
        var client = CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, Password));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private async Task AssertRefreshTokenRotationAsync()
    {
        using var client = CreateHttpsClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("teacher-one", Password));
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);

        var refreshResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest(login.RefreshToken));
        refreshResponse.EnsureSuccessStatusCode();
        var replacement = await refreshResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(replacement);
        Assert.NotEqual(login.RefreshToken, replacement.RefreshToken);

        var replayResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest(login.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
        await AssertProblemCodeAsync(replayResponse, "invalid_refresh_token");
    }

    private static async Task AssertProblemCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(expectedCode, problem.Extensions["code"]?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(problem.Extensions["traceId"]?.ToString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.Instance));
    }

    private HttpClient CreateHttpsClient() => _factory!.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task SeedUsersAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var role in new[] { RoleNames.Teacher, RoleNames.Student })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                Assert.True((await roleManager.CreateAsync(new IdentityRole<Guid>(role))).Succeeded);
            }
        }

        await CreateUserAsync(userManager, "teacher-one", "Teacher One", RoleNames.Teacher);
        await CreateUserAsync(userManager, "teacher-two", "Teacher Two", RoleNames.Teacher);
        await CreateUserAsync(userManager, "student-one", "Student One", RoleNames.Student, "STU-001");
    }

    private static async Task CreateUserAsync(
        UserManager<ApplicationUser> userManager,
        string userName,
        string name,
        string role,
        string? studentCode = null)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Name = name,
            StudentCode = studentCode
        };
        Assert.True((await userManager.CreateAsync(user, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, role)).Succeeded);
    }

    private async Task ExecuteAdminCommandAsync(string commandText)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync();
    }
}
