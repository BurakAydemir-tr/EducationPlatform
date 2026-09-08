using EducationPlatform.Api.Common.Results;

namespace EducationPlatform.Api.Tests.Common.Results;

public sealed class ErrorTests
{
    [Fact]
    public void Constructor_WithNullCode_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new Error(null!, "Description", ErrorType.Validation));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankCode_Throws(string code) =>
        Assert.Throws<ArgumentException>(() => new Error(code, "Description", ErrorType.Validation));

    [Fact]
    public void Constructor_WithNullDescription_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new Error("code", null!, ErrorType.Validation));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankDescription_Throws(string description) =>
        Assert.Throws<ArgumentException>(() => new Error("code", description, ErrorType.Validation));
}
