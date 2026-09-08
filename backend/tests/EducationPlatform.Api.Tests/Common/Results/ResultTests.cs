using EducationPlatform.Api.Common.Results;

namespace EducationPlatform.Api.Tests.Common.Results;

public sealed class ResultTests
{
    [Fact]
    public void Success_CreatesSuccessfulResult()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void GenericSuccess_CarriesValue()
    {
        var result = Result<string>.Success("value");

        Assert.True(result.IsSuccess);
        Assert.Equal("value", result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_WithNullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
    }

    [Fact]
    public void GenericFailure_WithNullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string>.Failure(null!));
    }

    [Fact]
    public void Failure_CarriesStableError()
    {
        var error = new Error("test_conflict", "The operation conflicts with current state.", ErrorType.Conflict);

        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void GenericFailure_CarriesStableError()
    {
        var error = new Error("test_conflict", "The operation conflicts with current state.", ErrorType.Conflict);
        var result = Result<string>.Failure(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
    }
}
