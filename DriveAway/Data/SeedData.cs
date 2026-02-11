using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace DriveAway.Data
{
    public static class SeedData
    {
        public static async Task Initialize(IServiceProvider serviceProvider)
        {
            // Get the RoleManager and UserManager from the dependency injection container
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // NOTE: If you are using a custom user class (e.g., ApplicationUser) instead of the default,
            // change <IdentityUser> to <ApplicationUser> in the line below.
            var userManager = serviceProvider.GetRequiredService<UserManager<IdentityUser>>();

            // --- Configuration ---
            string roleName = "Super Admin";
            string adminEmail = "admin@gmail.com";
            string adminPassword = "Password123!"; // Must meet complexity requirements

            // 1. Create the Role if it does not exist
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }

            // 2. Create the Super Admin User if they do not exist
            if (await userManager.FindByEmailAsync(adminEmail) == null)
            {
                var adminUser = new IdentityUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true // Auto-confirm so they can login
                };

                var createResult = await userManager.CreateAsync(adminUser, adminPassword);

                // 3. Assign the "Super Admin" role to the new user
                if (createResult.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, roleName);
                }
            }
        }
    }
}