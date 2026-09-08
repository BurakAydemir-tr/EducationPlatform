using System.Net;
using System.Text.Json;
using EducationPlatform.Api.Common.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EducationPlatform.Api.Tests.Common.Errors;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task UnexpectedException_ReturnsSafeProblemDetailsWithStatus500()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = CreateContext();

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("sensitive exception detail"),
            CancellationToken.None);
        var response = await ReadProblemDetails(context);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(StatusCodes.Status500InternalServerError, response.Status);
        Assert.Equal("internal_error", response.Extensions["code"]?.ToString());
        Assert.Equal("test-trace", response.Extensions["traceId"]?.ToString());
        Assert.Equal("/test", response.Instance);
        Assert.DoesNotContain("sensitive exception detail", response.Detail);
    }

    [Fact]
    public async Task ExceptionMiddleware_UsesHandlerThroughRealHttpPipeline()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.MapGet("/throws", () => ThrowUnexpectedException());
        app.Urls.Add("http://127.0.0.1:0");

        await app.StartAsync();
        try
        {
            var server = app.Services.GetRequiredService<IServer>();
            var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
            using var client = new HttpClient();

            var httpResponse = await client.GetAsync($"{address}/throws");
            var body = await httpResponse.Content.ReadAsStringAsync();
            var response = JsonSerializer.Deserialize<ProblemDetails>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.Equal(HttpStatusCode.InternalServerError, httpResponse.StatusCode);
            var problemDetails = Assert.IsType<ProblemDetails>(response);
            Assert.Equal("internal_error", problemDetails.Extensions["code"]?.ToString());
            Assert.False(string.IsNullOrWhiteSpace(problemDetails.Extensions["traceId"]?.ToString()));
            Assert.Equal("/throws", problemDetails.Instance);
            Assert.DoesNotContain("pipeline secret", body);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static DefaultHttpContext CreateContext()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = "test-trace"
        };
        context.Request.Path = "/test";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<ProblemDetails> ReadProblemDetails(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            context.Response.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return Assert.IsType<ProblemDetails>(response);
    }

    private static IResult ThrowUnexpectedException() =>
        throw new InvalidOperationException("pipeline secret");
}
