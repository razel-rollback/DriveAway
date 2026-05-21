// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DriveAway.Models;
using DriveAway.Services;

namespace DriveAway.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private const string IncrementalLockoutLevelClaimType = "driveaway:lockout-level";
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly IAuditService _audit;
        private readonly ITurnstileService _turnstileService;
        private readonly IdentityOptions _identityOptions;

        public LoginModel(
            SignInManager<IdentityUser> signInManager,
            ILogger<LoginModel> logger,
            IAuditService audit,
            ITurnstileService turnstileService,
            IOptions<IdentityOptions> identityOptions)
        {
            _signInManager = signInManager;
            _logger = logger;
            _audit = audit;
            _turnstileService = turnstileService;
            _identityOptions = identityOptions.Value;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string ReturnUrl { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [TempData]
        public string ErrorMessage { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl ??= Url.Content("~/");

            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                var turnstileToken = Request.Form["cf-turnstile-response"];
                var isTurnstileValid = await _turnstileService.VerifyTokenAsync(turnstileToken);
                if (!isTurnstileValid)
                {
                    await _audit.LogAsync(AuditAction.LoginFailed, AuditModule.Authentication,
                        details: "Turnstile verification failed.",
                        userEmailOverride: Input.Email);
                    ModelState.AddModelError(string.Empty, "Turnstile verification failed. Please try again.");
                    return Page();
                }

                var user = await _signInManager.UserManager.FindByEmailAsync(Input.Email);
                if (user != null)
                {
                    if (!user.LockoutEnabled)
                    {
                        user.LockoutEnabled = true;
                        await _signInManager.UserManager.UpdateAsync(user);
                    }

                    // Check if the account is archived/deactivated (LockoutEnd is close to MaxValue)
                    if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow.AddYears(100))
                    {
                        _logger.LogWarning("User account is deactivated.");
                        ModelState.AddModelError(string.Empty, "Your account has been deactivated. Please contact an administrator.");
                        return Page();
                    }

                    // Check if the user is already locked out
                    var isAlreadyLockedOut = await _signInManager.UserManager.IsLockedOutAsync(user);
                    if (isAlreadyLockedOut)
                    {
                        var lockoutEnd = await _signInManager.UserManager.GetLockoutEndDateAsync(user);
                        if (lockoutEnd.HasValue)
                        {
                            var timeLeft = lockoutEnd.Value - DateTimeOffset.UtcNow;
                            if (timeLeft.TotalSeconds > 0)
                            {
                                var timeSpanStr = "";
                                if (timeLeft.TotalHours >= 1)
                                {
                                    var hours = (int)Math.Ceiling(timeLeft.TotalHours);
                                    timeSpanStr = $"{hours} {(hours == 1 ? "hour" : "hours")}";
                                }
                                else if (timeLeft.TotalMinutes >= 1)
                                {
                                    var minutes = (int)Math.Ceiling(timeLeft.TotalMinutes);
                                    timeSpanStr = $"{minutes} {(minutes == 1 ? "minute" : "minutes")}";
                                }
                                else
                                {
                                    var seconds = (int)Math.Ceiling(timeLeft.TotalSeconds);
                                    timeSpanStr = $"{seconds} {(seconds == 1 ? "second" : "seconds")}";
                                }

                                ModelState.AddModelError(string.Empty, $"This account is locked out. Please try again in {timeSpanStr}.");
                                return Page();
                            }
                        }
                    }
                }

                var result = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);
                if (result.IsLockedOut)
                {
                    if (user != null)
                    {
                        await ApplyIncrementalLockoutAsync(user);
                        
                        // Refresh user after applying incremental lockout to get the updated LockoutEnd
                        user = await _signInManager.UserManager.FindByEmailAsync(Input.Email);
                    }

                    var failedAttempts = user != null
                        ? await _signInManager.UserManager.GetAccessFailedCountAsync(user)
                        : 0;

                    await _audit.LogAsync(AuditAction.LoginFailed, AuditModule.Authentication,
                        details: failedAttempts > 0
                            ? $"Account locked out after {failedAttempts} failed attempts."
                            : "Account locked out after multiple failed attempts.",
                        userEmailOverride: Input.Email);

                    _logger.LogWarning("User account locked out due to multiple failed attempts.");

                    var lockoutEnd = user != null ? await _signInManager.UserManager.GetLockoutEndDateAsync(user) : null;
                    if (lockoutEnd.HasValue)
                    {
                        var timeLeft = lockoutEnd.Value - DateTimeOffset.UtcNow;
                        if (timeLeft.TotalSeconds > 0)
                        {
                            var timeSpanStr = "";
                            if (timeLeft.TotalHours >= 1)
                            {
                                var hours = (int)Math.Ceiling(timeLeft.TotalHours);
                                timeSpanStr = $"{hours} {(hours == 1 ? "hour" : "hours")}";
                            }
                            else if (timeLeft.TotalMinutes >= 1)
                                {
                                    var minutes = (int)Math.Ceiling(timeLeft.TotalMinutes);
                                    timeSpanStr = $"{minutes} {(minutes == 1 ? "minute" : "minutes")}";
                                }
                            else
                            {
                                var seconds = (int)Math.Ceiling(timeLeft.TotalSeconds);
                                timeSpanStr = $"{seconds} {(seconds == 1 ? "second" : "seconds")}";
                            }

                            ModelState.AddModelError(string.Empty, $"Account locked out due to multiple failed attempts. Please try again in {timeSpanStr}.");
                            return Page();
                        }
                    }

                    ModelState.AddModelError(string.Empty, "Account locked out due to multiple failed attempts. Please try again later.");
                    return Page();
                }
                if (result.Succeeded)
                {
                    _logger.LogInformation("User logged in.");
                    await _audit.LogAsync(AuditAction.Login, AuditModule.Authentication,
                        details: "User logged in successfully.",
                        userEmailOverride: Input.Email);

                    // Check if user is Super Admin and redirect to dashboard
                    var currentUser = await _signInManager.UserManager.FindByEmailAsync(Input.Email);
                    if (currentUser != null)
                    {
                        await ResetIncrementalLockoutAsync(currentUser);
                    }

                    if (currentUser != null && await _signInManager.UserManager.IsInRoleAsync(currentUser, "Super Admin"))
                    {
                        return RedirectToAction("Dashboard", "SuperAdmin");
                    }
                    if (currentUser != null && await _signInManager.UserManager.IsInRoleAsync(currentUser, "Admin"))
                    {
                        return RedirectToAction("Dashboard", "Admin");
                    }
                    if (currentUser != null && await _signInManager.UserManager.IsInRoleAsync(currentUser, "Business Owner"))
                    {
                        return RedirectToAction("Dashboard", "BusinessOwner");
                    }
                    if (currentUser != null && await _signInManager.UserManager.IsInRoleAsync(currentUser, "Staff"))
                    {
                        return RedirectToAction("Dashboard", "StaffDashboard");
                    }
                    if (currentUser != null && await _signInManager.UserManager.IsInRoleAsync(currentUser, "Mechanic"))
                    {
                        return RedirectToAction("Dashboard", "Mechanic");
                    }

                    return LocalRedirect(returnUrl);
                }
                if (result.RequiresTwoFactor)
                {
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
                }
                else
                {
                    await _audit.LogAsync(AuditAction.LoginFailed, AuditModule.Authentication,
                        details: "Invalid login attempt.",
                        userEmailOverride: Input.Email);

                    if (user != null)
                    {
                        // Refresh the user to get the updated AccessFailedCount
                        user = await _signInManager.UserManager.FindByEmailAsync(Input.Email);
                        var maxAttempts = _identityOptions.Lockout.MaxFailedAccessAttempts;
                        var failedAttempts = await _signInManager.UserManager.GetAccessFailedCountAsync(user);
                        var attemptsLeft = maxAttempts - failedAttempts;

                        if (attemptsLeft > 0)
                        {
                            ModelState.AddModelError(string.Empty, $"Invalid login attempt. You have {attemptsLeft} {(attemptsLeft == 1 ? "attempt" : "attempts")} remaining before lockout.");
                            return Page();
                        }
                    }

                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    return Page();
                }
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }

        private async Task ApplyIncrementalLockoutAsync(IdentityUser user)
        {
            var claims = await _signInManager.UserManager.GetClaimsAsync(user);
            var levelClaim = claims.FirstOrDefault(c => c.Type == IncrementalLockoutLevelClaimType);

            var currentLevel = 0;
            if (levelClaim != null && int.TryParse(levelClaim.Value, out var parsedLevel))
            {
                currentLevel = Math.Max(0, parsedLevel);
            }

            var nextLevel = Math.Min(currentLevel + 1, 20);
            var baseMinutes = Math.Max(1, (int)_identityOptions.Lockout.DefaultLockoutTimeSpan.TotalMinutes);
            var lockoutMinutes = Math.Min(baseMinutes * nextLevel, 24 * 60);
            var lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(lockoutMinutes);

            await _signInManager.UserManager.SetLockoutEndDateAsync(user, lockoutEnd);

            var newLevelClaim = new Claim(IncrementalLockoutLevelClaimType, nextLevel.ToString());
            if (levelClaim == null)
            {
                await _signInManager.UserManager.AddClaimAsync(user, newLevelClaim);
            }
            else if (levelClaim.Value != newLevelClaim.Value)
            {
                await _signInManager.UserManager.ReplaceClaimAsync(user, levelClaim, newLevelClaim);
            }

            _logger.LogInformation(
                "Incremental lockout applied for user {UserId}. Level: {LockoutLevel}, DurationMinutes: {DurationMinutes}.",
                user.Id,
                nextLevel,
                lockoutMinutes);
        }

        private async Task ResetIncrementalLockoutAsync(IdentityUser user)
        {
            var claims = await _signInManager.UserManager.GetClaimsAsync(user);
            var levelClaim = claims.FirstOrDefault(c => c.Type == IncrementalLockoutLevelClaimType);
            if (levelClaim != null)
            {
                await _signInManager.UserManager.RemoveClaimAsync(user, levelClaim);
            }
        }
    }
}
