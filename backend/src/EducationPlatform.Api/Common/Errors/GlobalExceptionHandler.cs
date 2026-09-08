using Microsoft.AspNetCore.Diagnostics;

namespace EducationPlatform.Api.Common.Errors;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "Unhandled exception while processing {RequestPath}",
            httpContext.Request.Path);

        await ApiProblemDetails.InternalServerError(httpContext).ExecuteAsync(httpContext);

        return true;
    }
}
