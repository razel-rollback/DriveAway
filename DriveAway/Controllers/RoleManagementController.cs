using DriveAway.Models;
using DriveAway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Super Admin")]
    public class RoleManagementController : Controller
    {
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IAuditService _audit;

        public RoleManagementController(RoleManager<IdentityRole> roleManager, UserManager<IdentityUser> userManager, IAuditService audit)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _audit = audit;
        }

        public IActionResult Index()
        {
            var roles = _roleManager.Roles.ToList();
            return View(roles);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string roleName)
        {
            if (!string.IsNullOrWhiteSpace(roleName))
            {
                var roleExists = await _roleManager.RoleExistsAsync(roleName);
                if (!roleExists)
                {
                    var result = await _roleManager.CreateAsync(new IdentityRole(roleName));
                    if (result.Succeeded)
                    {
                        await _audit.LogAsync(AuditAction.Create, AuditModule.RoleManagement,
                            "Role", null, roleName, $"Role '{roleName}' created.");
                        TempData["Success"] = $"Role '{roleName}' created successfully!";
                        return RedirectToAction(nameof(Index));
                    }
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError("", error.Description);
                    }
                }
                else
                {
                    ModelState.AddModelError("", "Role already exists.");
                }
            }
            else
            {
                ModelState.AddModelError("", "Role name is required.");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, string roleName)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(roleName))
            {
                TempData["Error"] = "Role ID and name are required.";
                return RedirectToAction(nameof(Index));
            }

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
            {
                TempData["Error"] = "Role not found.";
                return RedirectToAction(nameof(Index));
            }

            // Prevent editing of system roles
            if (role.Name == "Super Admin" || role.Name == "Admin")
            {
                TempData["Error"] = $"Cannot edit the '{role.Name}' system role!";
                return RedirectToAction(nameof(Index));
            }

            // Check if another role already has the new name
            var existingRole = await _roleManager.FindByNameAsync(roleName);
            if (existingRole != null && existingRole.Id != id)
            {
                TempData["Error"] = $"Role '{roleName}' already exists.";
                return RedirectToAction(nameof(Index));
            }

            // Update the role name
            role.Name = roleName;
            role.NormalizedName = roleName.ToUpper();

            var result = await _roleManager.UpdateAsync(role);
            if (result.Succeeded)
            {
                await _audit.LogAsync(AuditAction.Update, AuditModule.RoleManagement,
                    "Role", role.Id, roleName, $"Role renamed to '{roleName}'.");
                TempData["Success"] = $"Role updated to '{roleName}' successfully!";
            }
            else
            {
                TempData["Error"] = "Failed to update role.";
                foreach (var error in result.Errors)
                {
                    // Log errors if needed
                    Console.WriteLine(error.Description);
                }
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            var role = await _roleManager.FindByIdAsync(id);
            if (role != null)
            {
                // Prevent deletion of system roles
                if (role.Name == "Super Admin" || role.Name == "Admin")
                {
                    TempData["Error"] = $"Cannot delete '{role.Name}' system role!";
                    return RedirectToAction(nameof(Index));
                }

                // Check if any users are assigned to this role
                var users = await _userManager.GetUsersInRoleAsync(role.Name);
                if (users.Any())
                {
                    TempData["Error"] = $"Cannot delete role '{role.Name}' because it has {users.Count()} user(s) assigned.";
                    return RedirectToAction(nameof(Index));
                }

                var result = await _roleManager.DeleteAsync(role);
                if (result.Succeeded)
                {
                    await _audit.LogAsync(AuditAction.Delete, AuditModule.RoleManagement,
                        "Role", role.Id, role.Name, $"Role '{role.Name}' deleted.");
                    TempData["Success"] = $"Role '{role.Name}' deleted successfully!";
                }
                else
                {
                    TempData["Error"] = "Failed to delete role.";
                }
            }
            else
            {
                TempData["Error"] = "Role not found.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}