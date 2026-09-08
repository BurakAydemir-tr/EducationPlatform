using EducationPlatform.Api.Common.Results;

namespace EducationPlatform.Api.Common.Errors;

internal static class ApiProblemDetails
{
    internal const string InternalErrorCode = "internal_error";

    public static IResult FromError(HttpContext httpContext, Error error)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(error);

        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Authentication => StatusCodes.Status401Unauthorized,
            ErrorType.Authorization => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Type, "Unsupported error type.")
        };

        return Create(httpContext, statusCode, GetTitle(error.Type), error.Description, error.Code);
    }

    public static IResult InternalServerError(HttpContext httpContext) =>
        Create(
            httpContext,
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred",
            "The request could not be completed due to an unexpected error.",
            InternalErrorCode);

    private static IResult Create(
        HttpContext httpContext,
        int statusCode,
        string title,
        string detail,
        string code) =>
        Microsoft.AspNetCore.Http.Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            instance: httpContext.Request.Path,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["traceId"] = httpContext.TraceIdentifier
            });

    private static string GetTitle(ErrorType type) => type switch
    {
        ErrorType.Validation => "Validation failed",
        ErrorType.Authentication => "Authentication required",
        ErrorType.Authorization => "Access forbidden",
        ErrorType.NotFound => "Resource not found",
        ErrorType.Conflict => "Operation conflict",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported error type.")
    };
}
