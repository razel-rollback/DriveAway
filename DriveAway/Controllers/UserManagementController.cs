using DriveAway.Data;
using DriveAway.Models;
using DriveAway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Super Admin,Admin,Business Owner")]
    public class UserManagementController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IAuditService _audit;
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _email;

        public UserManagementController(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager, IAuditService audit, ApplicationDbContext context, IEmailService email)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _audit = audit;
            _context = context;
            _email = email;
        }

        public async Task<IActionResult> Index()
        {
            var users = new List<IdentityUser>();
            
            // Get the current user's branch (for admin filtering)
            var currentUser = await _userManager.GetUserAsync(User);
            var currentUserBranch = await _context.UserBranches
                .Include(ub => ub.Branch)
                .FirstOrDefaultAsync(ub => ub.UserId == currentUser.Id);

            if (User.IsInRole("Super Admin"))
            {
                // Super Admin sees everyone
                users = await _userManager.Users.ToListAsync();
            }
            else if (User.IsInRole("Business Owner"))
            {
                // Business Owner sees Admin, Staff, and Mechanic. Also sees Business Owners (disabled actions).
                var allUsers = await _userManager.Users.ToListAsync();
                foreach(var u in allUsers)
                {
                    var roles = await _userManager.GetRolesAsync(u);
                    if (roles.Contains("Staff") || roles.Contains("Mechanic") || roles.Contains("Admin") || roles.Contains("Business Owner"))
                    {
                        if (!roles.Contains("Super Admin"))
                        {
                            users.Add(u);
                        }
                    }
                }
            }
            else if (User.IsInRole("Admin"))
            {
                // Admin sees only Staff and Mechanics in their own branch, plus other Admins (disabled)
                var allUsers = await _userManager.Users.ToListAsync();
                foreach(var u in allUsers)
                {
                    var roles = await _userManager.GetRolesAsync(u);
                    if (roles.Contains("Staff") || roles.Contains("Mechanic"))
                    {
                        if (!roles.Contains("Super Admin") && !roles.Contains("Business Owner"))
                        {
                            // Only include if they belong to the admin's branch
                            if (currentUserBranch != null && currentUserBranch.BranchId != null)
                            {
                                var userBranch = await _context.UserBranches
                                    .FirstOrDefaultAsync(ub => ub.UserId == u.Id);
                                if (userBranch != null && userBranch.BranchId == currentUserBranch.BranchId)
                                {
                                    users.Add(u);
                                }
                            }
                        }
                    }
                    else if (roles.Contains("Admin"))
                    {
                        // Show admin accounts (actions disabled) — only same branch
                        if (!roles.Contains("Super Admin") && !roles.Contains("Business Owner"))
                        {
                            if (currentUserBranch != null && currentUserBranch.BranchId != null)
                            {
                                var userBranch = await _context.UserBranches
                                    .FirstOrDefaultAsync(ub => ub.UserId == u.Id);
                                if (userBranch != null && userBranch.BranchId == currentUserBranch.BranchId)
                                {
                                    users.Add(u);
                                }
                            }
                        }
                    }
                }
            }

            // Filter out archived users (LockoutEnd == MaxValue)
            users = users.Where(u => !(u.LockoutEnabled && u.LockoutEnd.HasValue && u.LockoutEnd.Value.Year >= 9999)).ToList();

            var userViewModels = new List<UserViewModel>();

            foreach (var user in users)
            {
                var thisViewModel = new UserViewModel();
                thisViewModel.Id = user.Id;
                thisViewModel.Email = user.Email;
                thisViewModel.UserName = user.UserName;
                var roles = await _userManager.GetRolesAsync(user);
                thisViewModel.Roles = roles;
                thisViewModel.IsBusinessOwner = roles.Contains("Business Owner");
                thisViewModel.IsActive = !(user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow);

                // Get branch name
                var userBranch = await _context.UserBranches
                    .Include(ub => ub.Branch)
                    .FirstOrDefaultAsync(ub => ub.UserId == user.Id);
                thisViewModel.BranchName = userBranch?.Branch?.Name;

                userViewModels.Add(thisViewModel);
            }

            return View(userViewModels);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var allRoles = _roleManager.Roles.ToList();
            var allowedRoles = new List<IdentityRole>();

            if (User.IsInRole("Super Admin"))
            {
                allowedRoles = allRoles;
            }
            else if (User.IsInRole("Business Owner"))
            {
                allowedRoles = allRoles.Where(r => r.Name == "Admin" || r.Name == "Staff" || r.Name == "Mechanic").ToList();
            }
            else if (User.IsInRole("Admin"))
            {
                allowedRoles = allRoles.Where(r => r.Name == "Staff" || r.Name == "Mechanic").ToList();
            }

            ViewBag.Roles = new SelectList(allowedRoles, "Name", "Name");
            await PopulateBranchesViewBag();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateUserViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = new IdentityUser { UserName = model.Email, Email = model.Email, EmailConfirmed = true };
                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    if (!string.IsNullOrEmpty(model.SelectedRole))
                    {
                        await _userManager.AddToRoleAsync(user, model.SelectedRole);
                    }

                    // Save branch assignment
                    int? branchIdToAssign = model.BranchId;

                    // If Admin is creating, auto-assign their own branch
                    if (User.IsInRole("Admin") && !User.IsInRole("Super Admin") && !User.IsInRole("Business Owner"))
                    {
                        var currentUser = await _userManager.GetUserAsync(User);
                        var currentUserBranch = await _context.UserBranches.FirstOrDefaultAsync(ub => ub.UserId == currentUser.Id);
                        branchIdToAssign = currentUserBranch?.BranchId;
                    }

                    // Don't assign branch for Super Admin or Business Owner roles
                    if (model.SelectedRole != "Super Admin" && model.SelectedRole != "Business Owner" && branchIdToAssign != null)
                    {
                        _context.UserBranches.Add(new UserBranch
                        {
                            UserId = user.Id,
                            BranchId = branchIdToAssign
                        });
                        await _context.SaveChangesAsync();
                    }

                    await _audit.LogAsync(AuditAction.Create, AuditModule.UserManagement, "User",
                        user.Id, user.Email,
                        $"User created with role: {model.SelectedRole ?? "None"}.");

                    // Send welcome email to the new user
                    var branchName = "";
                    if (branchIdToAssign.HasValue)
                    {
                        var branch = await _context.Branches.FindAsync(branchIdToAssign.Value);
                        branchName = branch?.Name ?? "";
                    }
                    var welcomeEmailBody = $@"
                        <h2>Welcome to DriveAway!</h2>
                        <p>Hello,</p>
                        <p>Your account has been created. Here are your details:</p>
                        <table style='border-collapse:collapse;'>
                            <tr><td style='padding:4px 12px;'><strong>Email:</strong></td><td>{model.Email}</td></tr>
                            <tr><td style='padding:4px 12px;'><strong>Role:</strong></td><td>{model.SelectedRole ?? "N/A"}</td></tr>
                            {(string.IsNullOrEmpty(branchName) ? "" : $"<tr><td style='padding:4px 12px;'><strong>Branch:</strong></td><td>{branchName}</td></tr>")}
                        </table>
                        <p>Please log in and change your password as soon as possible.</p>";
                    try { await _email.SendEmailAsync(model.Email, "Welcome to DriveAway — Account Created", welcomeEmailBody); }
                    catch { /* logged by SmtpEmailService */ }

                    TempData["Success"] = "User created successfully!";
                    return RedirectToAction("Index");
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            var allRoles = _roleManager.Roles.ToList();
            var allowedRoles = new List<IdentityRole>();

            if (User.IsInRole("Super Admin"))
            {
                allowedRoles = allRoles;
            }
            else if (User.IsInRole("Business Owner"))
            {
                allowedRoles = allRoles.Where(r => r.Name == "Admin" || r.Name == "Staff" || r.Name == "Mechanic").ToList();
            }
            else if (User.IsInRole("Admin"))
            {
                allowedRoles = allRoles.Where(r => r.Name == "Staff" || r.Name == "Mechanic").ToList();
            }

            ViewBag.Roles = new SelectList(allowedRoles, "Name", "Name");
            await PopulateBranchesViewBag();
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var userRoles = await _userManager.GetRolesAsync(user);
            var userBranch = await _context.UserBranches.FirstOrDefaultAsync(ub => ub.UserId == user.Id);

            var model = new EditUserViewModel
            {
                Id = user.Id,
                Email = user.Email,
                SelectedRole = userRoles.FirstOrDefault(),
                BranchId = userBranch?.BranchId,
                IsActive = !(user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
            };

            var allRoles = _roleManager.Roles.ToList();
            var allowedRoles = new List<IdentityRole>();

            if (User.IsInRole("Super Admin"))
            {
                allowedRoles = allRoles;
            }
            else if (User.IsInRole("Business Owner"))
            {
                if (userRoles.Contains("Business Owner")) 
                {
                    return Forbid(); // Business owner cannot edit themselves or other business owners here, only in profile.
                }
                allowedRoles = allRoles.Where(r => r.Name == "Admin" || r.Name == "Staff" || r.Name == "Mechanic").ToList();
            }
            else if (User.IsInRole("Admin"))
            {
                if (userRoles.Contains("Admin") || userRoles.Contains("Business Owner")) return Forbid();
                allowedRoles = allRoles.Where(r => r.Name == "Staff" || r.Name == "Mechanic").ToList();
            }

            ViewBag.Roles = new SelectList(allowedRoles, "Name", "Name");
            await PopulateBranchesViewBag();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditUserViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByIdAsync(model.Id);
                if (user == null)
                {
                    return NotFound();
                }

                user.Email = model.Email;
                user.UserName = model.Email;

                // Update active status
                if (model.IsActive)
                {
                    user.LockoutEnd = null;
                }
                else
                {
                    user.LockoutEnabled = true;
                    user.LockoutEnd = DateTimeOffset.MaxValue;
                }

                var result = await _userManager.UpdateAsync(user);

                if (result.Succeeded)
                {
                    var userRoles = await _userManager.GetRolesAsync(user);
                    if (!string.IsNullOrEmpty(model.SelectedRole))
                    {
                         if (!userRoles.Contains(model.SelectedRole))
                         {
                             await _userManager.RemoveFromRolesAsync(user, userRoles);
                             await _userManager.AddToRoleAsync(user, model.SelectedRole);
                         }
                    }
                    else if (userRoles.Any()) 
                    {
                         await _userManager.RemoveFromRolesAsync(user, userRoles);
                    }

                    // Update branch assignment
                    var existingBranch = await _context.UserBranches.FirstOrDefaultAsync(ub => ub.UserId == user.Id);

                    // Don't assign branch for Super Admin or Business Owner roles
                    if (model.SelectedRole == "Super Admin" || model.SelectedRole == "Business Owner")
                    {
                        // Remove branch if switching to top-level role
                        if (existingBranch != null)
                        {
                            _context.UserBranches.Remove(existingBranch);
                        }
                    }
                    else
                    {
                        int? branchIdToAssign = model.BranchId;

                        // If Admin is editing, force their own branch
                        if (User.IsInRole("Admin") && !User.IsInRole("Super Admin") && !User.IsInRole("Business Owner"))
                        {
                            var currentUser = await _userManager.GetUserAsync(User);
                            var currentUserBranch = await _context.UserBranches.FirstOrDefaultAsync(ub => ub.UserId == currentUser.Id);
                            branchIdToAssign = currentUserBranch?.BranchId;
                        }

                        if (branchIdToAssign != null)
                        {
                            if (existingBranch != null)
                            {
                                existingBranch.BranchId = branchIdToAssign;
                                _context.UserBranches.Update(existingBranch);
                            }
                            else
                            {
                                _context.UserBranches.Add(new UserBranch
                                {
                                    UserId = user.Id,
                                    BranchId = branchIdToAssign
                                });
                            }
                        }
                        else if (existingBranch != null)
                        {
                            _context.UserBranches.Remove(existingBranch);
                        }
                    }
                    await _context.SaveChangesAsync();

                    TempData["Success"] = "User updated successfully!";
                    await _audit.LogAsync(AuditAction.Update, AuditModule.UserManagement, "User",
                        user.Id, user.Email,
                        $"User details/role updated. New role: {model.SelectedRole ?? "None"}.");
                    return RedirectToAction("Index");
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            var allRoles = _roleManager.Roles.ToList();
            var allowedRoles = new List<IdentityRole>();

            if (User.IsInRole("Super Admin"))
            {
                allowedRoles = allRoles;
            }
            else if (User.IsInRole("Business Owner"))
            {
                allowedRoles = allRoles.Where(r => r.Name == "Admin" || r.Name == "Staff" || r.Name == "Mechanic").ToList();
            }
            else if (User.IsInRole("Admin"))
            {
                allowedRoles = allRoles.Where(r => r.Name == "Rental Staff" || r.Name == "Mechanic").ToList();
            }

            ViewBag.Roles = new SelectList(allowedRoles, "Name", "Name");
            await PopulateBranchesViewBag();
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }
            return View(user);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user != null)
            {
                 // Prevent deleting yourself? Optional but good practice.
                 if (user.UserName == User.Identity.Name)
                 {
                     TempData["Error"] = "You cannot delete your own account.";
                     return RedirectToAction("Index");
                 }

                 var userRoles = await _userManager.GetRolesAsync(user);
                 if (User.IsInRole("Business Owner") && userRoles.Contains("Business Owner"))
                 {
                     TempData["Error"] = "You don't have permission to delete a Business Owner account.";
                     return RedirectToAction("Index");
                 }
                 if (User.IsInRole("Admin") && (userRoles.Contains("Business Owner") || userRoles.Contains("Admin") || userRoles.Contains("Super Admin")))
                 {
                     TempData["Error"] = "You don't have permission to delete this account.";
                     return RedirectToAction("Index");
                 }

                // Remove branch assignment first
                var userBranch = await _context.UserBranches.FirstOrDefaultAsync(ub => ub.UserId == user.Id);
                if (userBranch != null)
                {
                    _context.UserBranches.Remove(userBranch);
                    await _context.SaveChangesAsync();
                }

                var result = await _userManager.DeleteAsync(user);
                if (result.Succeeded)
                {
                    await _audit.LogAsync(AuditAction.Delete, AuditModule.UserManagement, "User",
                        user.Id, user.Email, "User account deleted.");
                    TempData["Success"] = "User deleted successfully!";
                }
                else
                {
                    TempData["Error"] = "Error deleting user.";
                }
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            if (user.UserName == User.Identity.Name)
            {
                TempData["Error"] = "You cannot archive your own account.";
                return RedirectToAction("Index");
            }

            var userRoles = await _userManager.GetRolesAsync(user);
            if (User.IsInRole("Business Owner") && userRoles.Contains("Business Owner"))
            {
                TempData["Error"] = "You don't have permission to archive a Business Owner account.";
                return RedirectToAction("Index");
            }
            if (User.IsInRole("Admin") && (userRoles.Contains("Business Owner") || userRoles.Contains("Admin") || userRoles.Contains("Super Admin")))
            {
                TempData["Error"] = "You don't have permission to archive this account.";
                return RedirectToAction("Index");
            }

            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.MaxValue;
            await _userManager.UpdateAsync(user);

            await _audit.LogAsync(AuditAction.Archive, AuditModule.UserManagement, "User",
                user.Id, user.Email, "User account archived.");

            TempData["Success"] = "User archived successfully!";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            user.LockoutEnd = null;
            await _userManager.UpdateAsync(user);

            await _audit.LogAsync(AuditAction.Update, AuditModule.UserManagement, "User",
                user.Id, user.Email, "User account restored from archive.");

            TempData["Success"] = "User restored successfully!";
            return RedirectToAction("Index");
        }

        private async Task PopulateBranchesViewBag()
        {
            var branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();
            ViewBag.Branches = new SelectList(branches, "Id", "Name");

            // If the current user is an Admin, pass their branch info
            if (User.IsInRole("Admin") && !User.IsInRole("Super Admin") && !User.IsInRole("Business Owner"))
            {
                var currentUser = await _userManager.GetUserAsync(User);
                var currentUserBranch = await _context.UserBranches
                    .Include(ub => ub.Branch)
                    .FirstOrDefaultAsync(ub => ub.UserId == currentUser.Id);
                ViewBag.AdminBranchId = currentUserBranch?.BranchId;
                ViewBag.AdminBranchName = currentUserBranch?.Branch?.Name ?? "No branch assigned";
                ViewBag.IsAdminUser = true;
            }
            else
            {
                ViewBag.IsAdminUser = false;
            }
        }
    }
}
