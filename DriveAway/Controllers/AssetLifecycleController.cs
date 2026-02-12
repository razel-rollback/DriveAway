using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AssetLifecycleController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Acquisition()
        {
            return View();
        }

        public IActionResult Registration()
        {
            return View();
        }

        public IActionResult Disposal()
        {
            return View();
        }
    }
}
