using DriveAway.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
            string[] roles = { "Super Admin", "Business Owner", "Admin", "Staff", "Mechanic" };
            
            // Super Admin credentials
            string superAdminEmail = "sadmin@gmail.com";
            string superAdminPassword = "Pass123.";
            
            // Default Admin (admin Owner) credentials - for testing/demo
            string adminEmail = "admin@gmail.com";
            string adminPassword = "Pass123.";

            // Default Business Owner credentials
            string businessOwnerEmail = "owner@gmail.com";
            string businessOwnerPassword = "Pass123.";


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

            // 3.5 Create a default Business Owner User if they do not exist
            if (await userManager.FindByEmailAsync(businessOwnerEmail) == null)
            {
                var ownerUser = new IdentityUser
                {
                    UserName = businessOwnerEmail,
                    Email = businessOwnerEmail,
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(ownerUser, businessOwnerPassword);

                if (createResult.Succeeded)
                {
                    await userManager.AddToRoleAsync(ownerUser, "Business Owner");
                }
            }

            // 4. Seed default Category Rates if none exist
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await context.CategoryRates.AnyAsync())
            {
                context.CategoryRates.AddRange(
                    new CategoryRate { Category = "Economy",        DailyRate = 1500m , IsArchived = false},
                    new CategoryRate { Category = "Compact",        DailyRate = 1800m , IsArchived = false},
                    new CategoryRate { Category = "Intermediate",   DailyRate = 2200m , IsArchived = false},
                    new CategoryRate { Category = "Standard",       DailyRate = 2500m , IsArchived = false},
                    new CategoryRate { Category = "SUV/Crossover",  DailyRate = 3500m , IsArchived = false},
                    new CategoryRate { Category = "Van/Minivan",    DailyRate = 3200m , IsArchived = false},
                    new CategoryRate { Category = "Premium/Luxury", DailyRate = 5000m , IsArchived = false},
                    new CategoryRate { Category = "Pickup",         DailyRate = 3000m , IsArchived = false},
                    new CategoryRate { Category = "Other",          DailyRate = 2000m , IsArchived = false}
                );
                await context.SaveChangesAsync();
            }
        }
    }
}
