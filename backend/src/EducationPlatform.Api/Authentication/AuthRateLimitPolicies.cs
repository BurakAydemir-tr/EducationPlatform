using System.Threading.RateLimiting;

namespace EducationPlatform.Api.Authentication;

public static class AuthRateLimitPolicies
{
    public const string Login = "auth-login";
    public const string Registration = "auth-registration";
    public const string Refresh = "auth-refresh";

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static FixedWindowRateLimiterOptions LoginOptions => CreateOptions(10);

    public static FixedWindowRateLimiterOptions RegistrationOptions => CreateOptions(20);

    public static FixedWindowRateLimiterOptions RefreshOptions => CreateOptions(20);

    private static FixedWindowRateLimiterOptions CreateOptions(int permitLimit) => new()
    {
        PermitLimit = permitLimit,
        Window = Window,
        QueueLimit = 0,
        AutoReplenishment = true
    };
}
