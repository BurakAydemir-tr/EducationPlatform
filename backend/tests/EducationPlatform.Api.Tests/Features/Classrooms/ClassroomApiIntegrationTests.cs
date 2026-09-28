using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Features.Auth;
using EducationPlatform.Api.Features.Admin;
using EducationPlatform.Api.Features.Classrooms;
using EducationPlatform.Api.Features.Students;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Api.Persistence.Identity;
using EducationPlatform.Api.Persistence.SeedData;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace EducationPlatform.Api.Tests.Features.Classrooms;

public sealed partial class ClassroomApiIntegrationTests : IAsyncLifetime
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
        var invalidClassroom = await teacherClient.PostAsJsonAsync(
            "/api/classrooms", new CreateClassroomRequest(new string('x', 201)));
        Assert.Equal(HttpStatusCode.BadRequest, invalidClassroom.StatusCode);
        await AssertProblemCodeAsync(invalidClassroom, "invalid_classroom");
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

        var classrooms = await teacherClient.GetFromJsonAsync<List<ClassroomSummaryResponse>>("/api/classrooms");
        Assert.Contains(classrooms!, item => item.Id == classroom.Id && item.ActiveStudentCount == 1);
        var classroomDetail = await teacherClient.GetFromJsonAsync<ClassroomDetailResponse>($"/api/classrooms/{classroom.Id}");
        Assert.Equal("6-A", classroomDetail!.Name);
        var activeStudents = await teacherClient.GetFromJsonAsync<List<ClassroomStudentResponse>>($"/api/classrooms/{classroom.Id}/students");
        Assert.Equal(createdStudent.Id, Assert.Single(activeStudents!).StudentId);

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
        Assert.Equal(HttpStatusCode.Forbidden, (await otherTeacherClient.GetAsync($"/api/classrooms/{classroom.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherTeacherClient.GetAsync($"/api/classrooms/{classroom.Id}/students")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await studentClient.GetAsync("/api/classrooms")).StatusCode);

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

        activeStudents = await teacherClient.GetFromJsonAsync<List<ClassroomStudentResponse>>($"/api/classrooms/{classroom.Id}/students");
        Assert.Single(activeStudents!);

        await AssertRefreshTokenRotationAsync();
    }

    [Fact]
    public async Task AuthenticationRejectsInvalidExpiredAndRevokedCredentials()
    {
        using var client = CreateHttpsClient();
        var invalid = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("teacher-one", "wrong-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        await AssertProblemCodeAsync(invalid, "invalid_credentials");

        var expiredValue = "expired-refresh-token";
        var revokedValue = "revoked-refresh-token";
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>();
            var userId = await db.Users.Where(user => user.UserName == "teacher-one").Select(user => user.Id).SingleAsync();
            db.RefreshTokens.AddRange(
                new RefreshToken { Id = Guid.NewGuid(), UserId = userId, TokenHash = TokenService.HashRefreshToken(expiredValue), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) },
                new RefreshToken { Id = Guid.NewGuid(), UserId = userId, TokenHash = TokenService.HashRefreshToken(revokedValue), ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), RevokedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        foreach (var token in new[] { expiredValue, revokedValue })
        {
            var response = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(token));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            await AssertProblemCodeAsync(response, "invalid_refresh_token");
        }
    }

    [Fact]
    public async Task Logout_RevokesOnlyOwnedSession_AndIsIdempotent()
    {
        using var publicClient = CreateHttpsClient();
        var first = await LoginAsync(publicClient, "student-one", Password);
        var second = await LoginAsync(publicClient, "student-one", Password);
        var otherUser = await LoginAsync(publicClient, "teacher-one", Password);
        using var student = CreateTokenClient(first.AccessToken);

        var logout = await student.PostAsJsonAsync("/api/auth/logout", new LogoutRequest(first.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await student.PostAsJsonAsync("/api/auth/logout", new LogoutRequest(first.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await student.PostAsJsonAsync("/api/auth/logout", new LogoutRequest(otherUser.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await student.PostAsJsonAsync("/api/auth/logout", new LogoutRequest("unknown-refresh-token"))).StatusCode);

        await AssertInvalidRefreshAsync(publicClient, first.RefreshToken);
        Assert.Equal(HttpStatusCode.OK,
            (await publicClient.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(second.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await publicClient.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(otherUser.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_UsesIdentityRules_AndRevokesAllRefreshTokens()
    {
        using var publicClient = CreateHttpsClient();
        var first = await LoginAsync(publicClient, "student-one", Password);
        var second = await LoginAsync(publicClient, "student-one", Password);
        using var student = CreateTokenClient(first.AccessToken);

        var wrongCurrent = await student.PostAsJsonAsync(
            "/api/auth/change-password", new ChangePasswordRequest("wrong-password", "Different123!"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);
        await AssertProblemCodeAsync(wrongCurrent, "invalid_current_password");

        var weak = await student.PostAsJsonAsync(
            "/api/auth/change-password", new ChangePasswordRequest(Password, "short"));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        await AssertProblemCodeAsync(weak, "invalid_new_password");

        const string newPassword = "Different123!";
        Assert.Equal(HttpStatusCode.NoContent,
            (await student.PostAsJsonAsync(
                "/api/auth/change-password", new ChangePasswordRequest(Password, newPassword))).StatusCode);
        await AssertInvalidRefreshAsync(publicClient, first.RefreshToken);
        await AssertInvalidRefreshAsync(publicClient, second.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await publicClient.PostAsJsonAsync("/api/auth/login", new LoginRequest("student-one", Password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await publicClient.PostAsJsonAsync("/api/auth/login", new LoginRequest("student-one", newPassword))).StatusCode);

        var adminLogin = await LoginAsync(publicClient, "admin@example.com", Password);
        using var admin = CreateTokenClient(adminLogin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync(
                "/api/auth/change-password", new ChangePasswordRequest(Password, "AdminChanged123!"))).StatusCode);
        await AssertInvalidRefreshAsync(publicClient, adminLogin.RefreshToken);

        var teacherLogin = await LoginAsync(publicClient, "teacher-one", Password);
        using var activeTeacher = CreateTokenClient(teacherLogin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent,
            (await activeTeacher.PostAsJsonAsync(
                "/api/auth/change-password", new ChangePasswordRequest(Password, "TeacherChanged123!"))).StatusCode);
        await AssertInvalidRefreshAsync(publicClient, teacherLogin.RefreshToken);

        var pending = await CreateTeacherAsync("password.pending@example.com", TeacherAccountStatus.Pending);
        using var pendingClient = CreateTokenClient(CreateJwt(pending.Id, RoleNames.Teacher));
        var inactive = await pendingClient.PostAsJsonAsync(
            "/api/auth/change-password", new ChangePasswordRequest(Password, newPassword));
        Assert.Equal(HttpStatusCode.Forbidden, inactive.StatusCode);
        await AssertProblemCodeAsync(inactive, "teacher_account_not_active");
    }

    [Fact]
    public async Task AdminTeacherListAndPasswordReset_EnforceRoleTargetAndTokenRules()
    {
        using var publicClient = CreateHttpsClient();
        using var admin = await CreateAuthenticatedClient("admin@example.com");
        using var teacher = await CreateAuthenticatedClient("teacher-one");
        using var student = await CreateAuthenticatedClient("student-one");

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/admin/teachers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/admin/teachers")).StatusCode);
        var teachers = await admin.GetFromJsonAsync<List<TeacherAdminResponse>>("/api/admin/teachers");
        Assert.Contains(teachers!, item => item.Email == null && item.Name == "Teacher One");
        var listJson = await (await admin.GetAsync("/api/admin/teachers")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", listJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", listJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessFailedCount", listJson, StringComparison.OrdinalIgnoreCase);

        var teacherLogin = await LoginAsync(publicClient, "teacher-one", Password);
        var teacherId = await GetUserIdAsync("teacher-one");
        var weak = await admin.PostAsJsonAsync(
            $"/api/admin/teachers/{teacherId}/reset-password", new ResetTeacherPasswordRequest("short"));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        await AssertProblemCodeAsync(weak, "invalid_new_password");

        const string replacement = "AdminReset123!";
        var reset = await admin.PostAsJsonAsync(
            $"/api/admin/teachers/{teacherId}/reset-password", new ResetTeacherPasswordRequest(replacement));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.DoesNotContain(replacement, await reset.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AssertInvalidRefreshAsync(publicClient, teacherLogin.RefreshToken);
        using var existingAccessToken = CreateTokenClient(teacherLogin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await existingAccessToken.GetAsync("/api/classrooms")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await publicClient.PostAsJsonAsync("/api/auth/login", new LoginRequest("teacher-one", replacement))).StatusCode);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var storedTeacher = await users.FindByIdAsync(teacherId.ToString());
            Assert.Equal(TeacherAccountStatus.Active, storedTeacher!.TeacherAccountStatus);
            Assert.True(await users.IsInRoleAsync(storedTeacher, RoleNames.Teacher));
            Assert.False(storedTeacher.EmailConfirmed);
        }

        var studentId = await GetUserIdAsync("student-one");
        var adminId = await GetUserIdAsync("admin@example.com");
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync($"/api/admin/teachers/{studentId}/reset-password", new ResetTeacherPasswordRequest(replacement))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync($"/api/admin/teachers/{adminId}/reset-password", new ResetTeacherPasswordRequest(replacement))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.PostAsJsonAsync($"/api/admin/teachers/{teacherId}/reset-password", new ResetTeacherPasswordRequest(replacement))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await student.PostAsJsonAsync($"/api/admin/teachers/{teacherId}/reset-password", new ResetTeacherPasswordRequest(replacement))).StatusCode);
    }

    [Fact]
    public async Task LoginLockout_IsExplicitAndResetsFailuresAfterSuccess()
    {
        using var client = CreateHttpsClient();
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("teacher-two", "wrong-password"))).StatusCode);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByNameAsync("teacher-two");
            Assert.Equal(2, user!.AccessFailedCount);
        }

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("teacher-two", Password))).StatusCode);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByNameAsync("teacher-two");
            Assert.Equal(0, user!.AccessFailedCount);
        }

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("teacher-one", "wrong-password"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("teacher-one", Password))).StatusCode);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByNameAsync("teacher-one");
            Assert.True(await users.IsLockedOutAsync(user!));
            user!.LockoutEnd = DateTimeOffset.UtcNow.AddSeconds(-1);
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        }
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("teacher-one", Password))).StatusCode);
    }

    [Fact]
    public async Task AuthRateLimiting_ReturnsProblemDetailsWith429()
    {
        using var client = CreateHttpsClient();
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt <= 10; attempt++)
            response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("missing-user", "wrong-password"));

        Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
        await AssertProblemCodeAsync(response, "rate_limit_exceeded");
    }

    [Fact]
    public async Task JwtMiddleware_RejectsInvalidTokens_AndAcceptsAllRoles()
    {
        var teacherId = await GetUserIdAsync("teacher-one");
        var adminId = await GetUserIdAsync("admin@example.com");
        var studentId = await GetUserIdAsync("student-one");
        var now = DateTimeOffset.UtcNow;
        var invalidTokens = new[]
        {
            "not-a-jwt",
            CreateJwt(teacherId, RoleNames.Teacher, expires: now.AddMinutes(-2)),
            CreateJwt(teacherId, RoleNames.Teacher, issuer: "wrong-issuer"),
            CreateJwt(teacherId, RoleNames.Teacher, audience: "wrong-audience"),
            CreateJwt(teacherId, RoleNames.Teacher, signingKey: "different-signing-key-that-is-at-least-32-bytes"),
            CreateJwt(teacherId, RoleNames.Teacher, notBefore: now.AddMinutes(2))
        };

        foreach (var token in invalidTokens)
        {
            using var client = CreateTokenClient(token);
            var response = await client.GetAsync("/api/classrooms");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            await AssertProblemCodeAsync(response, "authentication_required");
        }

        using var admin = CreateTokenClient(CreateJwt(adminId, RoleNames.Admin));
        using var teacher = CreateTokenClient(CreateJwt(teacherId, RoleNames.Teacher));
        using var student = CreateTokenClient(CreateJwt(studentId, RoleNames.Student));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/teachers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync("/api/classrooms")).StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized,
            (await student.GetAsync($"/api/student/courses/{Guid.NewGuid()}/progress")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentRefresh_AllowsOnlyOneReplacement()
    {
        using var client = CreateHttpsClient();
        var login = await LoginAsync(client, "admin@example.com", Password);
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken)),
            client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
        var success = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var replacement = await success.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(replacement!.RefreshToken))).StatusCode);
        await AssertInvalidRefreshAsync(client, login.RefreshToken);
    }

    [Fact]
    public async Task TeacherRegistration_AdminLifecycle_AndTokenRulesAreEnforced()
    {
        using var publicClient = CreateHttpsClient();
        var invalidEmail = await publicClient.PostAsJsonAsync("/api/auth/teacher-registration",
            new TeacherRegistrationRequest("invalid", "Name", "Surname", Password));
        Assert.Equal(HttpStatusCode.BadRequest, invalidEmail.StatusCode);
        var weakPassword = await publicClient.PostAsJsonAsync("/api/auth/teacher-registration",
            new TeacherRegistrationRequest("weak@example.com", "Name", "Surname", "short"));
        Assert.Equal(HttpStatusCode.BadRequest, weakPassword.StatusCode);

        foreach (var invalidRequest in new[]
        {
            new TeacherRegistrationRequest("empty-name@example.com", " ", "Surname", Password),
            new TeacherRegistrationRequest("empty-surname@example.com", "Name", " ", Password),
            new TeacherRegistrationRequest("long-name@example.com", new string('n', 201), "Surname", Password),
            new TeacherRegistrationRequest("long-surname@example.com", "Name", new string('s', 201), Password),
            new TeacherRegistrationRequest($"{new string('e', 245)}@example.com", "Name", "Surname", Password)
        })
        {
            var response = await publicClient.PostAsJsonAsync("/api/auth/teacher-registration", invalidRequest);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await AssertProblemCodeAsync(response, "invalid_teacher_registration");
        }

        foreach (var collision in new[] { "identifier@example.com", "code@example.com" })
        {
            var response = await publicClient.PostAsJsonAsync("/api/auth/teacher-registration",
                new TeacherRegistrationRequest(collision, "Name", "Surname", Password));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            await AssertProblemCodeAsync(response, "teacher_account_exists");
        }

        const string email = "pending.teacher@example.com";
        var registration = await publicClient.PostAsJsonAsync("/api/auth/teacher-registration",
            new
            {
                Email = email,
                Name = "Pending",
                Surname = "Teacher",
                Password,
                Role = RoleNames.Admin,
                TeacherAccountStatus = TeacherAccountStatus.Active,
                EmailConfirmed = true
            });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var registered = await registration.Content.ReadFromJsonAsync<TeacherRegistrationResponse>();
        Assert.Equal(TeacherAccountStatus.Pending, registered!.AccountStatus);
        var registrationJson = await registration.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", registrationJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", registrationJson, StringComparison.OrdinalIgnoreCase);

        var duplicateRegistration = await publicClient.PostAsJsonAsync("/api/auth/teacher-registration",
            new TeacherRegistrationRequest(email.ToUpperInvariant(), "Duplicate", "Teacher", Password));
        Assert.Equal(HttpStatusCode.Conflict, duplicateRegistration.StatusCode);
        await AssertProblemCodeAsync(duplicateRegistration, "teacher_account_exists");

        var pendingLogin = await publicClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.Forbidden, pendingLogin.StatusCode);
        await AssertProblemCodeAsync(pendingLogin, "teacher_approval_pending");

        var concurrentEmail = $"concurrent.{Guid.NewGuid():N}@example.com";
        var concurrent = await Task.WhenAll(
            publicClient.PostAsJsonAsync("/api/auth/teacher-registration", new TeacherRegistrationRequest(concurrentEmail, "One", "Teacher", Password)),
            publicClient.PostAsJsonAsync("/api/auth/teacher-registration", new TeacherRegistrationRequest(concurrentEmail, "Two", "Teacher", Password)));
        Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.Conflict);

        using var admin = await CreateAuthenticatedClient("admin@example.com");
        using var student = await CreateAuthenticatedClient("student-one");
        using var existingTeacher = await CreateAuthenticatedClient("teacher-one");
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/admin/teachers/pending")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await existingTeacher.GetAsync("/api/admin/teachers/pending")).StatusCode);
        var pending = await admin.GetFromJsonAsync<List<TeacherAdminResponse>>("/api/admin/teachers/pending");
        Assert.Contains(pending!, item => item.UserId == registered.UserId);
        var detail = await admin.GetFromJsonAsync<TeacherAdminResponse>($"/api/admin/teachers/{registered.UserId}");
        Assert.Equal("Pending", detail!.Name);
        Assert.False(detail.EmailConfirmed);

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/admin/teachers/{registered.UserId}/approve", null)).StatusCode);
        var invalidApprove = await admin.PostAsync($"/api/admin/teachers/{registered.UserId}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, invalidApprove.StatusCode);
        await AssertProblemCodeAsync(invalidApprove, "invalid_account_status_transition");

        var activeLoginResponse = await publicClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.OK, activeLoginResponse.StatusCode);
        var activeLogin = await activeLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.InRange(activeLogin!.AccessTokenExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10.1));
        var activeRefreshResponse = await publicClient.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(activeLogin.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, activeRefreshResponse.StatusCode);
        var activeRefresh = await activeRefreshResponse.Content.ReadFromJsonAsync<LoginResponse>();
        using var activeTeacher = CreateHttpsClient();
        activeTeacher.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", activeLogin.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/admin/teachers/{registered.UserId}/disable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await activeTeacher.GetAsync("/api/classrooms")).StatusCode);
        var disabledLogin = await publicClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.Forbidden, disabledLogin.StatusCode);
        await AssertProblemCodeAsync(disabledLogin, "teacher_account_disabled");
        var disabledRefresh = await publicClient.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(activeRefresh!.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, disabledRefresh.StatusCode);
        await AssertProblemCodeAsync(disabledRefresh, "invalid_refresh_token");
        var revokedReplay = await publicClient.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(activeRefresh.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, revokedReplay.StatusCode);
        await AssertProblemCodeAsync(revokedReplay, "invalid_refresh_token");

        const string rejectedEmail = "rejected.teacher@example.com";
        var rejectedRegistration = await publicClient.PostAsJsonAsync("/api/auth/teacher-registration",
            new TeacherRegistrationRequest(rejectedEmail, "Rejected", "Teacher", Password));
        var rejected = await rejectedRegistration.Content.ReadFromJsonAsync<TeacherRegistrationResponse>();
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/admin/teachers/{rejected!.UserId}/reject", null)).StatusCode);
        var rejectedLogin = await publicClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(rejectedEmail, Password));
        Assert.Equal(HttpStatusCode.Forbidden, rejectedLogin.StatusCode);
        await AssertProblemCodeAsync(rejectedLogin, "teacher_account_rejected");

        await AssertInactiveTeacherRefreshIsRejectedAsync(email: "pending-refresh@example.com", TeacherAccountStatus.Pending,
            "teacher_approval_pending");
        await AssertInactiveTeacherRefreshIsRejectedAsync(email: rejectedEmail, TeacherAccountStatus.Rejected,
            "teacher_account_rejected");

        await using var scope = _factory!.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = await users.FindByNameAsync(email);
        Assert.Equal("Pending", stored!.Name);
        Assert.Equal("Teacher", stored.Surname);
        Assert.True(await users.IsInRoleAsync(stored, RoleNames.Teacher));
        Assert.False(stored.EmailConfirmed);
        Assert.All(await scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>().RefreshTokens
            .Where(token => token.UserId == stored.Id).ToListAsync(), token => Assert.NotNull(token.RevokedAt));
    }

    private async Task AssertInactiveTeacherRefreshIsRejectedAsync(
        string email,
        TeacherAccountStatus status,
        string expectedCode)
    {
        var tokenValue = $"inactive-teacher-refresh-token-{email}";
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByNameAsync(email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    Id = Guid.NewGuid(), UserName = email, Email = email,
                    Name = "Inactive", Surname = "Teacher", TeacherAccountStatus = status
                };
                Assert.True((await users.CreateAsync(user, Password)).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, RoleNames.Teacher)).Succeeded);
            }

            var db = scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>();
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(), UserId = user.Id,
                TokenHash = TokenService.HashRefreshToken(tokenValue),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(tokenValue));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemCodeAsync(response, expectedCode);
    }

    [Fact]
    public async Task AdminSeedData_IsIdempotent_AndDoesNotCreateDefaultsWhenMissing()
    {
        await using var initialScope = _factory!.Services.CreateAsyncScope();
        var initialCount = await initialScope.ServiceProvider
            .GetRequiredService<EducationPlatformDbContext>().Users.CountAsync();
        var missing = new ConfigurationBuilder().AddInMemoryCollection().Build();
        await AdminSeedData.InitializeAsync(_factory.Services, missing);
        Assert.Equal(initialCount, await initialScope.ServiceProvider
            .GetRequiredService<EducationPlatformDbContext>().Users.CountAsync());

        const string seedUserName = "seed-admin@example.com";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SeedData:Admin:Email"] = seedUserName,
            ["SeedData:Admin:Name"] = "Seed",
            ["SeedData:Admin:Surname"] = "Admin",
            ["SeedData:Admin:Password"] = Password
        }).Build();
        await AdminSeedData.InitializeAsync(_factory.Services, configuration);
        await AdminSeedData.InitializeAsync(_factory.Services, configuration);

        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var seeded = await users.FindByNameAsync(seedUserName);
        Assert.NotNull(seeded);
        Assert.True(await users.IsInRoleAsync(seeded!, RoleNames.Admin));
        Assert.Null(seeded.TeacherAccountStatus);
        Assert.Single(await scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>().Users
            .Where(user => user.NormalizedUserName == seedUserName.ToUpperInvariant()).ToListAsync());
    }

    [Fact]
    public async Task AdminSeedData_RejectsPartialConfigurationAndExistingStudent()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>();
        var initialCount = await db.Users.CountAsync();

        var partial = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SeedData:Admin:Email"] = "partial-admin@example.com"
        }).Build();
        var partialError = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminSeedData.InitializeAsync(_factory.Services, partial));
        Assert.Contains("Email, Name, Surname and Password together", partialError.Message);
        Assert.Equal(initialCount, await db.Users.CountAsync());

        var existingStudent = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SeedData:Admin:Email"] = "student-one",
            ["SeedData:Admin:Name"] = "Student",
            ["SeedData:Admin:Surname"] = "One",
            ["SeedData:Admin:Password"] = Password
        }).Build();
        var roleError = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminSeedData.InitializeAsync(_factory.Services, existingStudent));
        Assert.Contains("without the Admin role", roleError.Message);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var student = await users.FindByNameAsync("student-one");
        Assert.NotNull(student);
        Assert.False(await users.IsInRoleAsync(student!, RoleNames.Admin));
        Assert.Equal(initialCount, await db.Users.CountAsync());
    }

    [Fact]
    public async Task AdminSeedData_RollsBackUserWhenRoleAssignmentFails()
    {
        const string email = "rollback-admin@example.com";
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "AspNetUserRoles"
            ADD CONSTRAINT "CK_Test_RejectAdminRoleAssignment"
            CHECK ("RoleId" <> '6482b681-4a15-4e88-a822-fad3e70c4091'::uuid) NOT VALID;
            """);

        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SeedData:Admin:Email"] = email,
                ["SeedData:Admin:Name"] = "Rollback",
                ["SeedData:Admin:Surname"] = "Admin",
                ["SeedData:Admin:Password"] = Password
            }).Build();

            await Assert.ThrowsAnyAsync<Exception>(() => AdminSeedData.InitializeAsync(_factory.Services, configuration));
            db.ChangeTracker.Clear();
            Assert.False(await db.Users.AnyAsync(user => user.NormalizedUserName == email.ToUpperInvariant()));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "AspNetUserRoles" DROP CONSTRAINT IF EXISTS "CK_Test_RejectAdminRoleAssignment";
                """);
        }
    }

    [Fact]
    public async Task Migration_BackfillsExistingTeacherAsActive_WithoutInventingSurname()
    {
        var databaseName = $"{DatabasePrefix}migration_{Guid.NewGuid():N}";
        var connectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = databaseName,
            Pooling = false
        }.ConnectionString;
        await ExecuteAdminCommandAsync($"CREATE DATABASE \"{databaseName}\"");

        try
        {
            var options = new DbContextOptionsBuilder<EducationPlatformDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            await using (var db = new EducationPlatformDbContext(options))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync("20260910145739_AddQuizAttemptsAndContentProgress");
            }

            var teacherId = Guid.NewGuid();
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO "AspNetUsers"
                        ("Id", "Name", "UserName", "NormalizedUserName", "EmailConfirmed",
                         "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                    VALUES (@id, 'Existing Teacher', 'existing-teacher', 'EXISTING-TEACHER', FALSE,
                            FALSE, FALSE, TRUE, 0);
                    INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
                    SELECT @id, "Id" FROM "AspNetRoles" WHERE "NormalizedName" = 'TEACHER';
                    """, connection);
                insert.Parameters.AddWithValue("id", teacherId);
                await insert.ExecuteNonQueryAsync();
            }

            await using (var db = new EducationPlatformDbContext(options))
                await db.Database.MigrateAsync();

            await using (var db = new EducationPlatformDbContext(options))
            {
                var teacher = await db.Users.AsNoTracking().SingleAsync(user => user.Id == teacherId);
                Assert.Equal(TeacherAccountStatus.Active, teacher.TeacherAccountStatus);
                Assert.Null(teacher.Surname);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAdminCommandAsync($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
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
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services.AddSingleton<TimeProvider>(_authClock);
                services.AddDbContext<EducationPlatformDbContext>(options => options.AddInterceptors(_authCommands));
            });
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
        var login = await LoginAsync(client, userName, Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string userName, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task AssertInvalidRefreshAsync(HttpClient client, string token)
    {
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(token));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertProblemCodeAsync(response, "invalid_refresh_token");
    }

    private HttpClient CreateTokenClient(string accessToken)
    {
        var client = CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private async Task<Guid> GetUserIdAsync(string userName)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByNameAsync(userName))!.Id;
    }

    private async Task<ApplicationUser> CreateTeacherAsync(string email, TeacherAccountStatus status)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            Name = "Status",
            Surname = "Teacher",
            TeacherAccountStatus = status
        };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, RoleNames.Teacher)).Succeeded);
        return user;
    }

    private static string CreateJwt(
        Guid userId,
        string role,
        DateTimeOffset? expires = null,
        string issuer = "integration-tests",
        string audience = "integration-tests",
        string signingKey = "integration-test-signing-key-at-least-32-bytes",
        DateTimeOffset? notBefore = null)
    {
        var now = DateTimeOffset.UtcNow;
        var tokenExpires = expires ?? now.AddMinutes(10);
        var tokenNotBefore = notBefore ?? (tokenExpires <= now ? tokenExpires.AddMinutes(-1) : now.AddMinutes(-1));
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role)
            ],
            notBefore: tokenNotBefore.UtcDateTime,
            expires: tokenExpires.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
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
        foreach (var role in new[] { RoleNames.Admin, RoleNames.Teacher, RoleNames.Student })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                Assert.True((await roleManager.CreateAsync(new IdentityRole<Guid>(role))).Succeeded);
            }
        }

        await CreateUserAsync(userManager, "teacher-one", "Teacher One", RoleNames.Teacher);
        await CreateUserAsync(userManager, "teacher-two", "Teacher Two", RoleNames.Teacher);
        await CreateUserAsync(userManager, "student-one", "Student One", RoleNames.Student, "STU-001");
        await CreateUserAsync(userManager, "admin@example.com", "Admin User", RoleNames.Admin);
        await CreateUserAsync(userManager, "identifier@example.com", "Identifier Student", RoleNames.Student);
        await CreateUserAsync(userManager, "code-owner", "Code Student", RoleNames.Student, "code@example.com");
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
            StudentCode = studentCode,
            Email = role == RoleNames.Admin ? userName : null,
            Surname = role == RoleNames.Admin ? "Admin" : null,
            TeacherAccountStatus = role == RoleNames.Teacher ? TeacherAccountStatus.Active : null
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
