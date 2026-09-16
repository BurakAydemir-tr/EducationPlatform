using EducationPlatform.Api.Authentication;
using EducationPlatform.Api.Persistence.Identity;
using Microsoft.AspNetCore.Identity;

namespace EducationPlatform.Api.Persistence.SeedData;

public static class TeacherSeedData
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        var options = configuration.GetSection(TeacherSeedOptions.SectionName).Get<TeacherSeedOptions>()
            ?? new TeacherSeedOptions();
        var configuredValues = new[] { options.UserName, options.Name, options.Password };
        if (configuredValues.All(string.IsNullOrWhiteSpace)) return;
        if (configuredValues.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException(
                "SeedData Teacher configuration must provide UserName, Name and Password together.");

        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        if (!await roleManager.RoleExistsAsync(RoleNames.Teacher))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(RoleNames.Teacher));
            EnsureSucceeded(roleResult, "Teacher role could not be created.");
        }

        var userName = options.UserName!.Trim();
        var teacher = await userManager.FindByNameAsync(userName);
        if (teacher is not null)
        {
            if (!await userManager.IsInRoleAsync(teacher, RoleNames.Teacher))
                throw new InvalidOperationException(
                    "SeedData Teacher username belongs to an existing user without the Teacher role.");
            return;
        }

        teacher = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Name = options.Name!.Trim()
        };
        EnsureSucceeded(await userManager.CreateAsync(teacher, options.Password!), "Teacher user could not be created.");
        EnsureSucceeded(await userManager.AddToRoleAsync(teacher, RoleNames.Teacher), "Teacher role could not be assigned.");
    }

    private static void EnsureSucceeded(IdentityResult result, string message)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"{message} {string.Join(" ", result.Errors.Select(error => error.Description))}");
    }
}
