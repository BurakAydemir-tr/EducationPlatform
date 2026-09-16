namespace EducationPlatform.Api.Persistence.SeedData;

public sealed class TeacherSeedOptions
{
    public const string SectionName = "SeedData:Teacher";

    public string? UserName { get; init; }

    public string? Name { get; init; }

    public string? Password { get; init; }
}
