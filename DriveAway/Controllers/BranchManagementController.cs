using DriveAway.Data;
using DriveAway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Business Owner,Super Admin")]
    public class BranchManagementController : Controller
    {
        private readonly ApplicationDbContext _context;

        public BranchManagementController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var branches = await _context.Branches.ToListAsync();
            return View(branches);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Branch branch)
        {
            if (!ModelState.IsValid)
                return View(branch);

            branch.CreatedAt = DateTime.UtcNow;
            _context.Add(branch);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Branch created successfully.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (id == null) return NotFound();

            var branch = await _context.Branches.FindAsync(id);
            if (branch == null) return NotFound();

            return View(branch);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Branch branch)
        {
            if (id != branch.Id) return NotFound();

            if (!ModelState.IsValid)
                return View(branch);

            try
            {
                _context.Update(branch);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Branch updated successfully.";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!BranchExists(branch.Id)) return NotFound();
                else throw;
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(int id)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var branch = await _context.Branches.FindAsync(id);
            if (branch != null)
            {
                branch.IsActive = !branch.IsActive; // Toggle static switch
                _context.Update(branch);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Branch status updated successfully.";
            }
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Performance(int? id)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (id == null) return NotFound();
            var branch = await _context.Branches.FindAsync(id);
            if (branch == null) return NotFound();

            // Set static dummy KPIs for performance view for now
            ViewBag.BranchName = branch.Name;
            ViewBag.FleetSize = 12;
            ViewBag.ActiveRentals = 5;
            ViewBag.MonthlyRevenue = 150000m;

            return View(branch);
        }

        private bool BranchExists(int id)
        {
            return _context.Branches.Any(e => e.Id == id);
        }
    }
}
