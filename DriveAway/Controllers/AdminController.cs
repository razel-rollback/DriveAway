using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
       
        public IActionResult Dashboard()
        {
            return View();
        }
    }
}
