using System.Net;
using System.Net.Http.Json;
using EducationPlatform.Api.Features.Classrooms;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace EducationPlatform.Api.Tests.Features.Classrooms;

public sealed class ClassroomAuthorizationPipelineTests
{
    [Fact]
    public async Task CreateClassroom_WithoutAuthentication_ReturnsUnauthorized()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=not_used;Username=not_used;Password=not_used",
                    ["Jwt:Issuer"] = "pipeline-tests",
                    ["Jwt:Audience"] = "pipeline-tests",
                    ["Jwt:SigningKey"] = "pipeline-test-signing-key-at-least-32-bytes"
                })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.PostAsJsonAsync(
            "/api/classrooms",
            new CreateClassroomRequest("6-A"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)HttpStatusCode.Unauthorized, problem.Status);
        Assert.Equal("authentication_required", problem.Extensions["code"]?.ToString());
        Assert.True(problem.Extensions.ContainsKey("traceId"));
        Assert.Equal("/api/classrooms", problem.Instance);
    }
}
