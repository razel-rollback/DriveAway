using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace DriveAway.Areas.Identity.Pages.Account.Manage
{
    public class EnableEmailAuthenticatorModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<EnableEmailAuthenticatorModel> _logger;
        private readonly IEmailSender _emailSender;

        public EnableEmailAuthenticatorModel(
            UserManager<IdentityUser> userManager,
            ILogger<EnableEmailAuthenticatorModel> logger,
            IEmailSender emailSender)
        {
            _userManager = userManager;
            _logger = logger;
            _emailSender = emailSender;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        [TempData]
        public string StatusMessage { get; set; }

        public class InputModel
        {
            [Required]
            [StringLength(6, ErrorMessage = "The {0} must be {2} characters long.", MinimumLength = 6)]
            [DataType(DataType.Text)]
            [Display(Name = "Verification Code")]
            public string Code { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var code = await _userManager.GenerateTwoFactorTokenAsync(user, "Email");
            var email = await _userManager.GetEmailAsync(user);

            try
            {
                await _emailSender.SendEmailAsync(
                    email,
                    "Enable Two-Factor Authentication",
                    $"Your verification code is: {code}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending 2FA email");
                ModelState.AddModelError(string.Empty, "Failed to send the verification email. Please check your email configuration.");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Strip spaces and hyphens
            var verificationCode = Input.Code.Replace(" ", string.Empty).Replace("-", string.Empty);

            var is2faTokenValid = await _userManager.VerifyTwoFactorTokenAsync(
                user, "Email", verificationCode);

            if (!is2faTokenValid)
            {
                ModelState.AddModelError("Input.Code", "Verification code is invalid or expired.");
                return Page();
            }

            await _userManager.SetTwoFactorEnabledAsync(user, true);
            var userId = await _userManager.GetUserIdAsync(user);
            _logger.LogInformation("User with ID '{UserId}' has enabled 2FA with Email.", userId);

            StatusMessage = "Your email authentication app has been verified and enabled.";
            return RedirectToPage("./TwoFactorAuthentication");
        }
    }
}
