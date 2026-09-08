using EducationPlatform.Domain.Classrooms;

namespace EducationPlatform.Domain.Tests.Classrooms;

public sealed class ClassroomTests
{
    [Fact]
    public void Constructor_WithValidValues_CreatesClassroom()
    {
        var id = Guid.NewGuid();
        var teacherId = Guid.NewGuid();

        var classroom = new Classroom(id, "  6-A  ", teacherId);

        Assert.Equal(id, classroom.Id);
        Assert.Equal("6-A", classroom.Name);
        Assert.Equal(teacherId, classroom.TeacherId);
    }

    [Fact]
    public void Constructor_WithEmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new Classroom(Guid.Empty, "6-A", Guid.NewGuid()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankName_Throws(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new Classroom(Guid.NewGuid(), name!, Guid.NewGuid()));
    }

    [Fact]
    public void Constructor_WithEmptyTeacherId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new Classroom(Guid.NewGuid(), "6-A", Guid.Empty));
    }

}
