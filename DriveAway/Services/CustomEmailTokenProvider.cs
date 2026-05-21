using Microsoft.AspNetCore.Identity;
using System;
using System.Threading.Tasks;

namespace DriveAway.Services
{
    public class CustomEmailTokenProvider : IUserTwoFactorTokenProvider<IdentityUser>
    {
        public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<IdentityUser> manager, IdentityUser user)
        {
            return manager.GetEmailAsync(user).ContinueWith(t => !string.IsNullOrWhiteSpace(t.Result));
        }

        public async Task<string> GenerateAsync(string purpose, UserManager<IdentityUser> manager, IdentityUser user)
        {
            var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            await manager.SetAuthenticationTokenAsync(user, "CustomEmailMfa", "Code", code);
            await manager.SetAuthenticationTokenAsync(user, "CustomEmailMfa", "Expiration", DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"));
            return code;
        }

        public async Task<bool> ValidateAsync(string purpose, string token, UserManager<IdentityUser> manager, IdentityUser user)
        {
            var code = await manager.GetAuthenticationTokenAsync(user, "CustomEmailMfa", "Code");
            var expStr = await manager.GetAuthenticationTokenAsync(user, "CustomEmailMfa", "Expiration");

            if (code != token || string.IsNullOrEmpty(expStr)) return false;

            if (DateTimeOffset.TryParseExact(expStr, "O", null, System.Globalization.DateTimeStyles.RoundtripKind, out var exp))
            {
                if (DateTimeOffset.UtcNow <= exp)
                {
                    // valid!
                    await manager.RemoveAuthenticationTokenAsync(user, "CustomEmailMfa", "Code");
                    await manager.RemoveAuthenticationTokenAsync(user, "CustomEmailMfa", "Expiration");
                    return true;
                }
            }
            return false;
        }
    }
}
