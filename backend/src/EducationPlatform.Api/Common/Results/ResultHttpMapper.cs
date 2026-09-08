using EducationPlatform.Api.Common.Errors;

namespace EducationPlatform.Api.Common.Results;

public static class ResultHttpMapper
{
    public static IResult ToHttpResult(this Result result, HttpContext httpContext)
    {
        return result.IsSuccess
            ? Microsoft.AspNetCore.Http.Results.NoContent()
            : ApiProblemDetails.FromError(httpContext, GetFailureError(result));
    }

    public static IResult ToHttpResult<T>(this Result<T> result, HttpContext httpContext)
    {
        return result.IsSuccess
            ? Microsoft.AspNetCore.Http.Results.Ok(result.Value)
            : ApiProblemDetails.FromError(httpContext, GetFailureError(result));
    }

    public static IResult ToCreatedHttpResult<T>(
        this Result<T> result,
        HttpContext httpContext,
        string location)
    {
        return result.IsSuccess
            ? Microsoft.AspNetCore.Http.Results.Created(location, result.Value)
            : ApiProblemDetails.FromError(httpContext, GetFailureError(result));
    }

    private static Error GetFailureError(Result result) =>
        result.Error ?? throw new InvalidOperationException("A failed result must contain an error.");

    private static Error GetFailureError<T>(Result<T> result) =>
        result.Error ?? throw new InvalidOperationException("A failed result must contain an error.");
}
