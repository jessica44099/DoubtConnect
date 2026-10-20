
using Microsoft.AspNetCore.Identity;

namespace DoubtConnect.Api.Data;

public static class IdentitySeeder
{
    public static async Task SeedRolesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        string[] roles = { "Student", "Mentor" };

        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var result = await roleManager.CreateAsync(
                    new IdentityRole<Guid> { Name = roleName }
                );

                if (!result.Succeeded)
                {
                    var errors = string.Join(
                        "; ",
                        result.Errors.Select(error => error.Description)
                    );

                    throw new InvalidOperationException(
                        $"Could not create role {roleName}: {errors}"
                    );
                }
            }
        }
    }
}
