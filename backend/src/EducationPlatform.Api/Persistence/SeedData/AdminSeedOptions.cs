namespace EducationPlatform.Api.Persistence.SeedData;

public sealed class AdminSeedOptions
{
    public const string SectionName = "SeedData:Admin";
    public string? Email { get; init; }
    public string? Name { get; init; }
    public string? Surname { get; init; }
    public string? Password { get; init; }
}
