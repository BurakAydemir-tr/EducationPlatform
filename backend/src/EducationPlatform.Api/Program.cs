using System.Text;
using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Common.Errors;
using EducationPlatform.Api.Common.Results;
using EducationPlatform.Api.Features.Auth;
using EducationPlatform.Api.Features.Admin;
using EducationPlatform.Api.Features.Classrooms;
using EducationPlatform.Api.Features.Courses;
using EducationPlatform.Api.Features.Learning;
using EducationPlatform.Api.Features.Quizzes;
using EducationPlatform.Api.Features.Students;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Api.Persistence.Identity;
using EducationPlatform.Api.Persistence.SeedData;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddDbContext<EducationPlatformDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Connection string 'DefaultConnection' is not configured.");
    }

    options.UseNpgsql(connectionString);
});
builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Issuer)
            && !string.IsNullOrWhiteSpace(options.Audience)
            && !string.IsNullOrWhiteSpace(options.SigningKey)
            && Encoding.UTF8.GetByteCount(options.SigningKey) >= 32
            && options.AccessTokenMinutes > 0
            && options.RefreshTokenDays > 0,
        "JWT issuer, audience, a signing key of at least 32 bytes, and positive token lifetimes must be configured.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TokenService>();

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequiredLength = 8;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<EducationPlatformDbContext>()
    .AddDefaultTokenProviders()
    .AddSignInManager();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, configuredOptions) =>
    {
        var jwtOptions = configuredOptions.Value;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                var result = ApiProblemDetails.FromError(
                    context.HttpContext,
                    new Error(
                        "authentication_required",
                        "Authentication is required.",
                        ErrorType.Authentication));
                await result.ExecuteAsync(context.HttpContext);
            },
            OnForbidden = context =>
            {
                var result = ApiProblemDetails.FromError(
                    context.HttpContext,
                    new Error(
                        "forbidden",
                        "You are not authorized to perform this operation.",
                        ErrorType.Authorization));
                return result.ExecuteAsync(context.HttpContext);
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(AuthRateLimitPolicies.Login, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => AuthRateLimitPolicies.LoginOptions));
    options.AddPolicy(AuthRateLimitPolicies.Registration, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => AuthRateLimitPolicies.RegistrationOptions));
    options.AddPolicy(AuthRateLimitPolicies.Refresh, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => AuthRateLimitPolicies.RefreshOptions));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Too many requests",
            detail: "Too many requests were submitted. Try again later.",
            instance: context.HttpContext.Request.Path,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "rate_limit_exceeded",
                ["traceId"] = context.HttpContext.TraceIdentifier
            }).ExecuteAsync(context.HttpContext);
    };
});

var app = builder.Build();

await AdminSeedData.InitializeAsync(app.Services, app.Configuration);

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapClassroomEndpoints();
app.MapCourseEndpoints();
app.MapLearningEndpoints();
app.MapQuizEndpoints();
app.MapStudentEndpoints();

app.Run();

public partial class Program;
