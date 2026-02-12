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
            string[] roles = { "Super Admin", "Admin", "Staff", "Mechanic" };
            
            // Super Admin credentials
            string superAdminEmail = "sadmin@gmail.com";
            string superAdminPassword = "Password123!";
            
            // Default Admin (admin Owner) credentials - for testing/demo
            string adminEmail = "admin@gmail.com";
            string adminPassword = "Password123!";


            // 1. Create Roles if they do not exist
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            // 2. Create the Super Admin User if they do not exist
            if (await userManager.FindByEmailAsync(superAdminEmail) == null)
            {
                var superAdminUser = new IdentityUser
                {
                    UserName = superAdminEmail,
                    Email = superAdminEmail,
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(superAdminUser, superAdminPassword);

                if (createResult.Succeeded)
                {
                    await userManager.AddToRoleAsync(superAdminUser, "Super Admin");
                }
            }

            // 3. Create a default Admin User (Business Owner) if they do not exist
            if (await userManager.FindByEmailAsync(adminEmail) == null)
            {
                var adminUser = new IdentityUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(adminUser, adminPassword);

                if (createResult.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }
            }
        }
    }
}