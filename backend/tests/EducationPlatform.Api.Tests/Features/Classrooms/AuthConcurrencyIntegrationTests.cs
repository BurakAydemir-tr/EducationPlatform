using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using EducationPlatform.Api.Features.Auth;
using EducationPlatform.Api.Features.Admin;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Api.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EducationPlatform.Api.Tests.Features.Classrooms;

public sealed partial class ClassroomApiIntegrationTests
{
    private readonly AuthCommandInterceptor _authCommands = new();
    private readonly AuthTestClock _authClock = new();
    private const string ChangedPassword = "ChangedIntegration123!";

    [Theory]
    [InlineData("login", "change", true)]
    [InlineData("login", "change", false)]
    [InlineData("login", "reset", true)]
    [InlineData("login", "reset", false)]
    [InlineData("login", "disable", true)]
    [InlineData("login", "disable", false)]
    [InlineData("refresh", "change", true)]
    [InlineData("refresh", "change", false)]
    [InlineData("refresh", "reset", true)]
    [InlineData("refresh", "reset", false)]
    [InlineData("refresh", "disable", true)]
    [InlineData("refresh", "disable", false)]
    public async Task AuthConcurrency_SecurityGateSerializesTokenProduction(
        string producer, string operation, bool producerFirst)
    {
        using var client = CreateHttpsClient();
        var session = await LoginAsync(client, "teacher-one", Password);
        var other = await LoginAsync(client, "student-one", Password);
        using var teacher = CreateTokenClient(session.AccessToken);
        using var admin = await CreateAuthenticatedClient("admin@example.com");
        var userId = await GetUserIdAsync("teacher-one");
        var before = await TokenCountAsync(userId);

        Task<HttpResponseMessage> Produce() => producer == "login"
            ? client.PostAsJsonAsync("/api/auth/login", new LoginRequest("teacher-one", Password))
            : client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken));
        Task<HttpResponseMessage> Secure() => SecurityOperationAsync(operation, userId, teacher, admin);

        var responses = await RunAuthRaceAsync(userId,
            producerFirst ? Produce : Secure, producerFirst ? Secure : Produce);
        using var production = producerFirst ? responses.First : responses.Second;
        using var security = producerFirst ? responses.Second : responses.First;
        Assert.Equal(HttpStatusCode.NoContent, security.StatusCode);
        if (producerFirst)
        {
            Assert.Equal(HttpStatusCode.OK, production.StatusCode);
            var produced = (await production.Content.ReadFromJsonAsync<LoginResponse>())!;
            await AssertInvalidRefreshAsync(client, produced.RefreshToken);
        }
        else
        {
            var disabledLogin = producer == "login" && operation == "disable";
            Assert.Equal(disabledLogin ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, production.StatusCode);
            await AssertProblemCodeAsync(production, disabledLogin ? "teacher_account_disabled"
                : producer == "login" ? "invalid_credentials" : "invalid_refresh_token");
        }

        Assert.Equal(before + (producerFirst ? 1 : 0), await TokenCountAsync(userId));
        await AssertNoActiveTokensAsync(userId);
        await AssertInvalidRefreshAsync(client, session.RefreshToken);
        // Existing access tokens still work: the gate does not introduce per-request status checks.
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync("/api/classrooms")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(other.RefreshToken))).StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = (await users.FindByIdAsync(userId.ToString()))!;
        Assert.Equal(operation == "disable" ? TeacherAccountStatus.Disabled : TeacherAccountStatus.Active,
            stored.TeacherAccountStatus);
        Assert.True(await users.CheckPasswordAsync(stored, operation == "disable" ? Password : ChangedPassword));
    }

    [Theory]
    [InlineData("change", true)]
    [InlineData("change", false)]
    [InlineData("reset", true)]
    [InlineData("reset", false)]
    public async Task AuthConcurrency_DisableCannotBeOverwrittenByPasswordOperation(string operation, bool disableFirst)
    {
        using var teacher = await CreateAuthenticatedClient("teacher-one");
        using var admin = await CreateAuthenticatedClient("admin@example.com");
        var userId = await GetUserIdAsync("teacher-one");
        Task<HttpResponseMessage> Disable() => SecurityOperationAsync("disable", userId, teacher, admin);
        Task<HttpResponseMessage> PasswordOperation() => SecurityOperationAsync(operation, userId, teacher, admin);
        var responses = await RunAuthRaceAsync(userId,
            disableFirst ? Disable : PasswordOperation, disableFirst ? PasswordOperation : Disable);
        using var disabled = disableFirst ? responses.First : responses.Second;
        using var changed = disableFirst ? responses.Second : responses.First;
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        var rejectedChange = disableFirst && operation == "change";
        Assert.Equal(rejectedChange ? HttpStatusCode.Forbidden : HttpStatusCode.NoContent, changed.StatusCode);
        if (rejectedChange) await AssertProblemCodeAsync(changed, "teacher_account_not_active");
        await using var scope = _factory!.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(userId.ToString()))!;
        Assert.Equal(TeacherAccountStatus.Disabled, user.TeacherAccountStatus);
        Assert.True(await users.CheckPasswordAsync(user, rejectedChange ? Password : ChangedPassword));
        await AssertNoActiveTokensAsync(userId);
    }

    [Fact]
    public async Task AuthConcurrency_ConcurrentRefreshCreatesExactlyOneSuccessor()
    {
        using var client = CreateHttpsClient();
        var session = await LoginAsync(client, "student-one", Password);
        var userId = await GetUserIdAsync("student-one");
        var responses = await RunAuthRaceAsync(userId,
            () => client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken)),
            () => client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken)));
        using var first = responses.First;
        using var second = responses.Second;
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        await AssertProblemCodeAsync(second, "invalid_refresh_token");
        Assert.Equal(2, await TokenCountAsync(userId));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>();
        Assert.Equal(1, await db.RefreshTokens.CountAsync(token => token.UserId == userId && token.RevokedAt == null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AuthConcurrency_LogoutKeepsSingleTokenSemantics(bool logoutFirst)
    {
        using var client = CreateHttpsClient();
        var session = await LoginAsync(client, "student-one", Password);
        using var student = CreateTokenClient(session.AccessToken);
        var userId = await GetUserIdAsync("student-one");
        Task<HttpResponseMessage> Logout() => student.PostAsJsonAsync("/api/auth/logout", new LogoutRequest(session.RefreshToken));
        Task<HttpResponseMessage> Refresh() => client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken));
        var responses = await RunAuthRaceAsync(userId, logoutFirst ? Logout : Refresh, logoutFirst ? Refresh : Logout);
        using var logout = logoutFirst ? responses.First : responses.Second;
        using var refresh = logoutFirst ? responses.Second : responses.First;
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        if (logoutFirst)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
            await AssertProblemCodeAsync(refresh, "invalid_refresh_token");
            await AssertNoActiveTokensAsync(userId);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
            var successor = (await refresh.Content.ReadFromJsonAsync<LoginResponse>())!;
            Assert.Equal(HttpStatusCode.OK,
                (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(successor.RefreshToken))).StatusCode);
        }
    }

    [Fact]
    public async Task AuthConcurrency_RefreshRechecksExpirationAfterLockWait()
    {
        using var client = CreateHttpsClient();
        var session = await LoginAsync(client, "student-one", Password);
        var userId = await GetUserIdAsync("student-one");
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>();
        var expiration = _authClock.GetUtcNow().AddMinutes(1);
        await db.RefreshTokens.Where(token => token.UserId == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ExpiresAt, expiration));
        await using var blocker = new NpgsqlConnection(db.Database.GetConnectionString());
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("""SELECT "Id" FROM "AspNetUsers" WHERE "Id" = @id FOR UPDATE""", blocker, transaction);
        command.Parameters.AddWithValue("id", userId);
        await command.ExecuteScalarAsync();
        var pending = client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken));
        try
        {
            await WaitForAuthWaiterAsync(blocker.ProcessID);
            _authClock.Advance(TimeSpan.FromMinutes(2));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertProblemCodeAsync(response, "invalid_refresh_token");
        Assert.Equal(1, await TokenCountAsync(userId));
    }

    [Theory]
    [InlineData("change")]
    [InlineData("reset")]
    [InlineData("disable")]
    public async Task AuthConcurrency_DatabaseFailureRollsBackSecurityOperation(string operation)
    {
        using var client = CreateHttpsClient();
        var session = await LoginAsync(client, "teacher-one", Password);
        using var teacher = CreateTokenClient(session.AccessToken);
        using var admin = await CreateAuthenticatedClient("admin@example.com");
        var userId = await GetUserIdAsync("teacher-one");
        // Fail after password/status has been written, at the subsequent token-revoke command.
        _authCommands.FailNextTokenUpdate();
        using var response = await SecurityOperationAsync(operation, userId, teacher, admin);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertProblemCodeAsync(response, "internal_error");
        Assert.DoesNotContain("test-auth-database-failure", await response.Content.ReadAsStringAsync());
        await using var scope = _factory!.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(userId.ToString()))!;
        Assert.True(await users.CheckPasswordAsync(user, Password));
        Assert.Equal(TeacherAccountStatus.Active, user.TeacherAccountStatus);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken))).StatusCode);
    }

    [Theory]
    [InlineData("login")]
    [InlineData("refresh")]
    public async Task AuthConcurrency_TokenInsertFailureRollsBack(string producer)
    {
        using var client = CreateHttpsClient();
        var session = await LoginAsync(client, "student-one", Password);
        var userId = await GetUserIdAsync("student-one");
        _authCommands.FailNextTokenInsert();
        using var response = producer == "login"
            ? await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("student-one", Password))
            : await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertProblemCodeAsync(response, "internal_error");
        Assert.Equal(1, await TokenCountAsync(userId));
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken))).StatusCode);
    }

    [Theory]
    [InlineData("change")]
    [InlineData("reset")]
    public async Task AuthConcurrency_IdentityFailurePreservesPasswordAndTokens(string operation)
    {
        using var client = CreateHttpsClient();
        var session = await LoginAsync(client, "teacher-one", Password);
        using var teacher = CreateTokenClient(session.AccessToken);
        using var admin = await CreateAuthenticatedClient("admin@example.com");
        var userId = await GetUserIdAsync("teacher-one");
        using var response = operation == "change"
            ? await teacher.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(Password, "short"))
            : await admin.PostAsJsonAsync($"/api/admin/teachers/{userId}/reset-password", new ResetTeacherPasswordRequest("short"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemCodeAsync(response, "invalid_new_password");
        await using var scope = _factory!.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(userId.ToString()))!;
        Assert.True(await users.CheckPasswordAsync(user, Password));
        Assert.Equal(TeacherAccountStatus.Active, user.TeacherAccountStatus);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken))).StatusCode);
    }

    private static Task<HttpResponseMessage> SecurityOperationAsync(string operation, Guid userId, HttpClient teacher, HttpClient admin) =>
        operation switch
        {
            "change" => teacher.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(Password, ChangedPassword)),
            "reset" => admin.PostAsJsonAsync($"/api/admin/teachers/{userId}/reset-password", new ResetTeacherPasswordRequest(ChangedPassword)),
            "disable" => admin.PostAsync($"/api/admin/teachers/{userId}/disable", null),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private async Task<(HttpResponseMessage First, HttpResponseMessage Second)> RunAuthRaceAsync(
        Guid userId, Func<Task<HttpResponseMessage>> first, Func<Task<HttpResponseMessage>> second)
    {
        var pause = _authCommands.PauseNextUserLock(userId);
        var firstTask = first();
        Task<HttpResponseMessage>? secondTask = null;
        try
        {
            var holderPid = await pause.Acquired.Task.WaitAsync(TimeSpan.FromSeconds(15));
            secondTask = second();
            await WaitForAuthWaiterAsync(holderPid);
        }
        finally
        {
            pause.Release.TrySetResult();
            await firstTask.WaitAsync(TimeSpan.FromSeconds(15));
            if (secondTask is not null) await secondTask.WaitAsync(TimeSpan.FromSeconds(15));
        }
        return (await firstTask, await secondTask!);
    }

    private async Task WaitForAuthWaiterAsync(int holderPid)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                WHERE datname = @database AND wait_event_type = 'Lock'
                  AND query LIKE '%AspNetUsers%' AND query LIKE '%FOR UPDATE%'
                  AND @holder = ANY(pg_blocking_pids(pid)))
                """, connection);
            command.Parameters.AddWithValue("database", _databaseName!);
            command.Parameters.AddWithValue("holder", holderPid);
            if (await command.ExecuteScalarAsync(deadline.Token) is true) return;
            await Task.Delay(25, deadline.Token);
        }
    }

    private async Task<int> TokenCountAsync(Guid userId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>()
            .RefreshTokens.CountAsync(token => token.UserId == userId);
    }

    private async Task AssertNoActiveTokensAsync(Guid userId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var now = _authClock.GetUtcNow();
        Assert.False(await scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>()
            .RefreshTokens.AnyAsync(token => token.UserId == userId && token.RevokedAt == null && token.ExpiresAt > now));
    }

    private sealed class AuthTestClock : TimeProvider
    {
        private long _offsetTicks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref _offsetTicks));
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _offsetTicks, duration.Ticks);
    }

    private sealed class AuthLockPause(Guid userId)
    {
        public Guid UserId { get; } = userId;
        public TaskCompletionSource<int> Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class AuthCommandInterceptor : DbCommandInterceptor
    {
        private AuthLockPause? _pause;
        private int _failUpdate;
        private int _failInsert;
        public AuthLockPause PauseNextUserLock(Guid userId)
        {
            var pause = new AuthLockPause(userId);
            Volatile.Write(ref _pause, pause);
            return pause;
        }
        public void FailNextTokenUpdate() => Interlocked.Exchange(ref _failUpdate, 1);
        public void FailNextTokenInsert() => Interlocked.Exchange(ref _failInsert, 1);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            var pause = Volatile.Read(ref _pause);
            if (pause is not null && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                && command.CommandText.Contains("AspNetUsers", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, pause.UserId))
                && Interlocked.CompareExchange(ref _pause, null, pause) == pause)
            {
                pause.Acquired.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
                await pause.Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE \"RefreshTokens\"", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _failUpdate, 0) == 1)
                throw new NpgsqlException("test-auth-database-failure");
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            FailIfArmed(command);
            return ValueTask.FromResult(result);
        }

        private void FailIfArmed(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT INTO \"RefreshTokens\"", StringComparison.Ordinal)
                    && Interlocked.Exchange(ref _failInsert, 0) == 1)
                throw new NpgsqlException("test-auth-database-failure");
        }
    }
}
