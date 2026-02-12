using DriveAway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Super Admin,Admin")]
    public class UserManagementController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public UserManagementController(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<IActionResult> Index()
        {
            var users = new List<IdentityUser>();
            
            if (User.IsInRole("Super Admin"))
            {
                // Super Admin sees everyone
                users = await _userManager.Users.ToListAsync();
            }
            else if (User.IsInRole("Admin"))
            {
                // Admin sees only Staff and Mechanics, NOT other Admins or Super Admins
                // Optimization: Get all users first then filter in memory for now, 
                // or use a more complex query if performance becomes an issue.
                // Note: GetUsersInRoleAsync is one by one.
                // Better approach: Get all users, then check roles.
                
                var allUsers = await _userManager.Users.ToListAsync();
                foreach(var u in allUsers)
                {
                    var roles = await _userManager.GetRolesAsync(u);
                    if (roles.Contains("Rental Staff") || roles.Contains("Mechanic"))
                    {
                        users.Add(u);
                    }
                }
            }

            var userViewModels = new List<UserViewModel>();

            foreach (var user in users)
            {
                var thisViewModel = new UserViewModel();
                thisViewModel.Id = user.Id;
                thisViewModel.Email = user.Email;
                thisViewModel.UserName = user.UserName;
                thisViewModel.Roles = await _userManager.GetRolesAsync(user);
                userViewModels.Add(thisViewModel);
            }

            return View(userViewModels);
        }

        [HttpGet]
        public IActionResult Create()
        {
            var allRoles = _roleManager.Roles.ToList();
            var allowedRoles = new List<IdentityRole>();

            if (User.IsInRole("Super Admin"))
            {
                allowedRoles = allRoles;
            }
            else if (User.IsInRole("Admin"))
            {
                allowedRoles = allRoles.Where(r => r.Name == "Rental Staff" || r.Name == "Mechanic").ToList();
            }

            ViewBag.Roles = new SelectList(allowedRoles, "Name", "Name");
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
            else if (User.IsInRole("Admin"))
            {
                allowedRoles = allRoles.Where(r => r.Name == "Rental Staff" || r.Name == "Mechanic").ToList();
            }

            ViewBag.Roles = new SelectList(allowedRoles, "Name", "Name");
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
            var model = new EditUserViewModel
            {
                Id = user.Id,
                Email = user.Email,
                SelectedRole = userRoles.FirstOrDefault()
            };

            var allRoles = _roleManager.Roles.ToList();
            var allowedRoles = new List<IdentityRole>();

            if (User.IsInRole("Super Admin"))
            {
                allowedRoles = allRoles;
            }
            else if (User.IsInRole("Admin"))
            {
                allowedRoles = allRoles.Where(r => r.Name == "Rental Staff" || r.Name == "Mechanic").ToList();
            }

            ViewBag.Roles = new SelectList(allowedRoles, "Name", "Name");
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

                var result = await _userManager.UpdateAsync(user);

                if (result.Succeeded)
                {
                    var userRoles = await _userManager.GetRolesAsync(user);
                    if (!string.IsNullOrEmpty(model.SelectedRole))
                    {
                         if (!userRoles.Contains(model.SelectedRole))
                         {
                             // Remove from old roles and add to new one
                             // Assuming single role for now based on UI, but could be multiple. 
                             // Logic here replaces all roles with selected one.
                             await _userManager.RemoveFromRolesAsync(user, userRoles);
                             await _userManager.AddToRoleAsync(user, model.SelectedRole);
                         }
                    }
                    else if (userRoles.Any()) 
                    {
                         // If no role selected but user had roles, remove them? 
                         // Or keep existing? Let's assume we want to clear if none selected.
                         await _userManager.RemoveFromRolesAsync(user, userRoles);
                    }

                    TempData["Success"] = "User updated successfully!";
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
            else if (User.IsInRole("Admin"))
            {
                allowedRoles = allRoles.Where(r => r.Name == "Rental Staff" || r.Name == "Mechanic").ToList();
            }

            ViewBag.Roles = new SelectList(allowedRoles, "Name", "Name");
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

                var result = await _userManager.DeleteAsync(user);
                if (result.Succeeded)
                {
                    TempData["Success"] = "User deleted successfully!";
                }
                else
                {
                    TempData["Error"] = "Error deleting user.";
                }
            }
            return RedirectToAction("Index");
        }
    }
}
