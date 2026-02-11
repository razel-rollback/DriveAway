using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Super Admin")]
    public class SuperAdminController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public SuperAdminController(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<IActionResult> Dashboard()
        {
            var totalUsers = _userManager.Users.Count();
            var totalRoles = _roleManager.Roles.Count();
            var superAdmins = (await _userManager.GetUsersInRoleAsync("Super Admin")).Count;

            ViewBag.TotalUsers = totalUsers;
            ViewBag.TotalRoles = totalRoles;
            ViewBag.SuperAdmins = superAdmins;

            return View();
        }
    }
}
