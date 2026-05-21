using DriveAway.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
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

            // Default Admin (admin Owner) credentials - for testing/demo
            string adminEmail = "admin@gmail.com";

            // Default Business Owner credentials
            string businessOwnerEmail = "owner@gmail.com";

            // Read seed passwords from configuration or environment variables to avoid hard-coded credentials.
            // Set these in appsettings.Development.json or environment variables for local dev.
            var configuration = serviceProvider.GetService<IConfiguration>();
            string? superAdminPassword = configuration?["Seed:SuperAdminPassword"]
                ?? Environment.GetEnvironmentVariable("SEED_SUPERADMIN_PASSWORD");
            string? adminPassword = configuration?["Seed:AdminPassword"]
                ?? Environment.GetEnvironmentVariable("SEED_ADMIN_PASSWORD");
            string? businessOwnerPassword = configuration?["Seed:BusinessOwnerPassword"]
                ?? Environment.GetEnvironmentVariable("SEED_BUSINESS_OWNER_PASSWORD");

            // If any password is not provided, generate a secure random password and output a one-time console message.
            if (string.IsNullOrEmpty(superAdminPassword))
            {
                superAdminPassword = SeedDataHelpers.GenerateSecurePassword();
                Console.WriteLine($"[SeedData] Generated SuperAdmin password: {superAdminPassword}");
            }
            if (string.IsNullOrEmpty(adminPassword))
            {
                adminPassword = SeedDataHelpers.GenerateSecurePassword();
                Console.WriteLine($"[SeedData] Generated Admin password: {adminPassword}");
            }
            if (string.IsNullOrEmpty(businessOwnerPassword))
            {
                businessOwnerPassword = SeedDataHelpers.GenerateSecurePassword();
                Console.WriteLine($"[SeedData] Generated BusinessOwner password: {businessOwnerPassword}");
            }


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
                    await userManager.SetLockoutEnabledAsync(superAdminUser, true);

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
                    await userManager.SetLockoutEnabledAsync(adminUser, true);

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
                    await userManager.SetLockoutEnabledAsync(ownerUser, true);

                    await userManager.AddToRoleAsync(ownerUser, "Business Owner");
                }
            }

            // 4. Seed default Category Rates if none exist
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await context.CategoryRates.AnyAsync())
            {
                context.CategoryRates.AddRange(
                    new CategoryRate { Category = "Economy", DailyRate = 1500m, IsArchived = false },
                    new CategoryRate { Category = "Compact", DailyRate = 1800m, IsArchived = false },
                    new CategoryRate { Category = "Intermediate", DailyRate = 2200m, IsArchived = false },
                    new CategoryRate { Category = "Standard", DailyRate = 2500m, IsArchived = false },
                    new CategoryRate { Category = "SUV/Crossover", DailyRate = 3500m, IsArchived = false },
                    new CategoryRate { Category = "Van/Minivan", DailyRate = 3200m, IsArchived = false },
                    new CategoryRate { Category = "Premium/Luxury", DailyRate = 5000m, IsArchived = false },
                    new CategoryRate { Category = "Pickup", DailyRate = 3000m, IsArchived = false },
                    new CategoryRate { Category = "Other", DailyRate = 2000m, IsArchived = false }
                );
                await context.SaveChangesAsync();
            }
        }
    }
}

namespace DriveAway.Data
{
    // Helper methods for seeding
    public static class SeedDataHelpers
    {
        private static readonly char[] _pwChars = (
            "abcdefghijklmnopqrstuvwxyz" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
            "0123456789" +
            "!@#$%^&*()-_+=").ToCharArray();

        public static string GenerateSecurePassword(int length = 16)
        {
            if (length < 12) length = 12;
            var bytes = new byte[length];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            var chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                var idx = bytes[i] % _pwChars.Length;
                chars[i] = _pwChars[idx];
            }
            // Ensure at least one of each required category (upper, lower, digit, symbol)
            var password = new string(chars);
            if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || !password.Any(c => "!@#$%^&*()-_+=".Contains(c)))
            {
                // fallback: build guaranteed-complex password
                var rnd = new Random();
                var sb = new System.Text.StringBuilder();
                sb.Append((char)('A' + rnd.Next(0, 26)));
                sb.Append((char)('a' + rnd.Next(0, 26)));
                sb.Append((char)('0' + rnd.Next(0, 10)));
                sb.Append("!#@");
                while (sb.Length < length)
                    sb.Append(_pwChars[rnd.Next(_pwChars.Length)]);
                return sb.ToString();
            }
            return password;
        }
    }

}


