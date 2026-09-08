using System.Text.Json;
using EducationPlatform.Api.Common.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace EducationPlatform.Api.Tests.Common.Results;

public sealed class ResultHttpMapperTests
{
    public static TheoryData<ErrorType, int, string> ErrorMappings => new()
    {
        { ErrorType.Validation, StatusCodes.Status400BadRequest, "Validation failed" },
        { ErrorType.Authentication, StatusCodes.Status401Unauthorized, "Authentication required" },
        { ErrorType.Authorization, StatusCodes.Status403Forbidden, "Access forbidden" },
        { ErrorType.NotFound, StatusCodes.Status404NotFound, "Resource not found" },
        { ErrorType.Conflict, StatusCodes.Status409Conflict, "Operation conflict" }
    };

    [Fact]
    public void Success_MapsToNoContent()
    {
        var httpResult = Result.Success().ToHttpResult(CreateContext());

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(httpResult);
        Assert.Equal(StatusCodes.Status204NoContent, statusResult.StatusCode);
    }

    [Fact]
    public void GenericSuccess_MapsToOkWithValue()
    {
        var value = new { Name = "value" };

        var httpResult = Result<object>.Success(value).ToHttpResult(CreateContext());

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(httpResult);
        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(httpResult);
        Assert.Equal(StatusCodes.Status200OK, statusResult.StatusCode);
        Assert.Same(value, valueResult.Value);
    }

    [Fact]
    public void GenericSuccess_MapsToCreatedWithValueAndLocation()
    {
        var value = new { Name = "value" };

        var httpResult = Result<object>.Success(value)
            .ToCreatedHttpResult(CreateContext(), "/api/resources/1");

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(httpResult);
        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(httpResult);
        Assert.Equal(StatusCodes.Status201Created, statusResult.StatusCode);
        Assert.Same(value, valueResult.Value);
    }

    [Theory]
    [MemberData(nameof(ErrorMappings))]
    public async Task Failure_MapsToConsistentProblemDetails(
        ErrorType errorType,
        int expectedStatus,
        string expectedTitle)
    {
        var context = CreateContext();
        var result = Result.Failure(new Error("test_error", "Test error.", errorType));

        await result.ToHttpResult(context).ExecuteAsync(context);
        var problemDetails = await ReadProblemDetails(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal(expectedStatus, problemDetails.Status);
        Assert.Equal(expectedTitle, problemDetails.Title);
        Assert.Equal("Test error.", problemDetails.Detail);
        Assert.Equal("/test", problemDetails.Instance);
        Assert.Equal("test_error", problemDetails.Extensions["code"]?.ToString());
        Assert.Equal("test-trace", problemDetails.Extensions["traceId"]?.ToString());
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
}
