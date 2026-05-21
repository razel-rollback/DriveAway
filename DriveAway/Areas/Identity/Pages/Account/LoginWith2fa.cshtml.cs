using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace DriveAway.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginWith2faModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<LoginWith2faModel> _logger;
        private readonly IEmailSender _emailSender;

        public LoginWith2faModel(
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            ILogger<LoginWith2faModel> logger,
            IEmailSender emailSender)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _emailSender = emailSender;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public bool RememberMe { get; set; }

        public string ReturnUrl { get; set; }

        [TempData]
        public string StatusMessage { get; set; }

        public class InputModel
        {
            [Required]
            [StringLength(7, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            [DataType(DataType.Text)]
            [Display(Name = "Verification Code")]
            public string TwoFactorCode { get; set; }

            [Display(Name = "Remember this machine")]
            public bool RememberMachine { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(bool rememberMe, string returnUrl = null)
        {
            // Ensure the user has gone through the username & password screen first
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
            {
                throw new InvalidOperationException($"Unable to load two-factor authentication user.");
            }

            var code = await _userManager.GenerateTwoFactorTokenAsync(user, "Email");
            var email = await _userManager.GetEmailAsync(user);

            try
            {
                await _emailSender.SendEmailAsync(
                    email,
                    "Your login verification code",
                    $"Your verification code is: {code}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending 2FA login email");
                ModelState.AddModelError(string.Empty, "Failed to send the verification email. Please check your email configuration.");
            }

            ReturnUrl = returnUrl;
            RememberMe = rememberMe;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(bool rememberMe, string returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            returnUrl = returnUrl ?? Url.Content("~/");

            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
            {
                throw new InvalidOperationException($"Unable to load two-factor authentication user.");
            }

            var authenticatorCode = Input.TwoFactorCode.Replace(" ", string.Empty).Replace("-", string.Empty);

            var result = await _signInManager.TwoFactorSignInAsync("Email", authenticatorCode, rememberMe, Input.RememberMachine);

            if (result.Succeeded)
            {
                _logger.LogInformation("User with ID '{UserId}' logged in with 2fa.", user.Id);
                
                if (await _userManager.IsInRoleAsync(user, "Super Admin"))
                {
                    return RedirectToAction("Dashboard", "SuperAdmin");
                }
                if (await _userManager.IsInRoleAsync(user, "Admin"))
                {
                    return RedirectToAction("Dashboard", "Admin");
                }
                if (await _userManager.IsInRoleAsync(user, "Business Owner"))
                {
                    return RedirectToAction("Dashboard", "BusinessOwner");
                }
                if (await _userManager.IsInRoleAsync(user, "Staff"))
                {
                    return RedirectToAction("Dashboard", "StaffDashboard");
                }
                if (await _userManager.IsInRoleAsync(user, "Mechanic"))
                {
                    return RedirectToAction("Dashboard", "Mechanic");
                }
                
                return LocalRedirect(returnUrl);
            }
            else if (result.IsLockedOut)
            {
                _logger.LogWarning("User with ID '{UserId}' account locked out.", user.Id);
                return RedirectToPage("./Lockout");
            }
            else
            {
                _logger.LogWarning("Invalid authenticator code entered for user with ID '{UserId}'.", user.Id);
                ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
                return Page();
            }
        }

        public async Task<IActionResult> OnPostResendAsync(bool rememberMe, string returnUrl = null)
        {
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
            {
                throw new InvalidOperationException($"Unable to load two-factor authentication user.");
            }

            var code = await _userManager.GenerateTwoFactorTokenAsync(user, "Email");
            var email = await _userManager.GetEmailAsync(user);

            try
            {
                await _emailSender.SendEmailAsync(
                    email,
                    "Your login verification code",
                    $"Your verification code is: {code}");
                StatusMessage = "A new verification code has been sent to your email.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending 2FA login email");
                ModelState.AddModelError(string.Empty, "Failed to send the verification email. Please check your email configuration.");
            }

            ReturnUrl = returnUrl;
            RememberMe = rememberMe;

            return Page();
        }
    }
}
