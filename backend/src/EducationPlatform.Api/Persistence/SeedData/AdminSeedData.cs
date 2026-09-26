using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Persistence;
using EducationPlatform.Api.Persistence.Identity;
using Microsoft.AspNetCore.Identity;

namespace EducationPlatform.Api.Persistence.SeedData;

public static class AdminSeedData
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AdminSeedOptions.SectionName).Get<AdminSeedOptions>() ?? new();
        var values = new[] { options.Email, options.Name, options.Surname, options.Password };
        if (values.All(string.IsNullOrWhiteSpace)) return;
        if (values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("SeedData Admin configuration must provide Email, Name, Surname and Password together.");

        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<EducationPlatformDbContext>();
        if (!await roles.RoleExistsAsync(RoleNames.Admin))
            Ensure(await roles.CreateAsync(new IdentityRole<Guid>(RoleNames.Admin)), "Admin role could not be created.");

        var email = options.Email!.Trim();
        var admin = await users.FindByNameAsync(email);
        if (admin is not null)
        {
            if (!await users.IsInRoleAsync(admin, RoleNames.Admin))
                throw new InvalidOperationException("SeedData Admin email belongs to an existing user without the Admin role.");
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            admin = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = email, Email = email,
                Name = options.Name!.Trim(), Surname = options.Surname!.Trim()
            };
            Ensure(await users.CreateAsync(admin, options.Password!), "Admin user could not be created.");
            Ensure(await users.AddToRoleAsync(admin, RoleNames.Admin), "Admin role could not be assigned.");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static void Ensure(IdentityResult result, string message)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"{message} {string.Join(" ", result.Errors.Select(error => error.Description))}");
    }
}
