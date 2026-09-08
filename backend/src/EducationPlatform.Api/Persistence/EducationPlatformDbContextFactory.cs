using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace EducationPlatform.Api.Persistence;

public sealed class EducationPlatformDbContextFactory : IDesignTimeDbContextFactory<EducationPlatformDbContext>
{
    public EducationPlatformDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<EducationPlatformDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Configure ConnectionStrings:DefaultConnection in User Secrets or the " +
                "ConnectionStrings__DefaultConnection environment variable before running EF Core commands.");
        }

        var options = new DbContextOptionsBuilder<EducationPlatformDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new EducationPlatformDbContext(options);
    }
}
