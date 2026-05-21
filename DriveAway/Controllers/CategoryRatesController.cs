using DriveAway.Data;
using DriveAway.Models;
using DriveAway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Super Admin,Business Owner,Admin")]
    public class CategoryRatesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _audit;

        public CategoryRatesController(ApplicationDbContext context, IAuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        private bool CanModify =>
            User.IsInRole("Super Admin") || User.IsInRole("Business Owner");

        // GET: CategoryRates
        public async Task<IActionResult> Index()
        {
            var rates = await _context.CategoryRates
                .Where(r => !r.IsArchived)
                .OrderBy(r => r.Category)
                .ToListAsync();

            ViewBag.CanModify = CanModify;
            return View(rates);
        }

        // GET: CategoryRates/Create
        [HttpGet]
        public IActionResult Create()
        {
            if (!CanModify) return Forbid();
            return View();
        }

        // POST: CategoryRates/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CategoryRate model)
        {
            if (!CanModify) return Forbid();

            if (!ModelState.IsValid)
                return View(model);

            // Check for duplicate category name
            var exists = await _context.CategoryRates
                .AnyAsync(c => c.Category.ToLower() == model.Category.ToLower());
            if (exists)
            {
                ModelState.AddModelError("Category", "A category with this name already exists.");
                return View(model);
            }

            _context.CategoryRates.Add(model);
            await _context.SaveChangesAsync();

            await _audit.LogAsync(AuditAction.Create, AuditModule.CategoryManagement,
                "CategoryRate", model.Id.ToString(), model.Category,
                $"Created category '{model.Category}' with daily rate ₱{model.DailyRate:F2}.");

            TempData["Success"] = "Category created successfully!";
            return RedirectToAction(nameof(Index));
        }

        // GET: CategoryRates/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            if (!CanModify) return Forbid();

            var rate = await _context.CategoryRates.FindAsync(id);
            if (rate == null) return NotFound();

            return View(rate);
        }

        // POST: CategoryRates/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CategoryRate model)
        {
            if (!CanModify) return Forbid();
            if (id != model.Id) return NotFound();

            if (!ModelState.IsValid)
                return View(model);

            var existing = await _context.CategoryRates.FindAsync(id);
            if (existing == null) return NotFound();

            // Check duplicate (excluding self)
            var duplicate = await _context.CategoryRates
                .AnyAsync(c => c.Id != id && c.Category.ToLower() == model.Category.ToLower());
            if (duplicate)
            {
                ModelState.AddModelError("Category", "A category with this name already exists.");
                return View(model);
            }

            var oldCategory = existing.Category;
            var oldRate = existing.DailyRate;

            existing.Category = model.Category;
            existing.DailyRate = model.DailyRate;

            await _context.SaveChangesAsync();

            await _audit.LogAsync(AuditAction.Update, AuditModule.CategoryManagement,
                "CategoryRate", existing.Id.ToString(), existing.Category,
                $"Updated category from '{oldCategory}' (₱{oldRate:F2}) to '{existing.Category}' (₱{existing.DailyRate:F2}).");

            TempData["Success"] = "Category updated successfully!";
            return RedirectToAction(nameof(Index));
        }

        // GET: CategoryRates/Delete/5
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            if (!CanModify) return Forbid();

            var rate = await _context.CategoryRates.FindAsync(id);
            if (rate == null) return NotFound();

            return View(rate);
        }

        // POST: CategoryRates/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!CanModify) return Forbid();

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var rate = await _context.CategoryRates.FindAsync(id);
            if (rate == null) return NotFound();

            _context.CategoryRates.Remove(rate);
            await _context.SaveChangesAsync();

            await _audit.LogAsync(AuditAction.Delete, AuditModule.CategoryManagement,
                "CategoryRate", rate.Id.ToString(), rate.Category,
                $"Deleted category '{rate.Category}' with daily rate ₱{rate.DailyRate:F2}.");

            TempData["Success"] = "Category deleted successfully!";
            return RedirectToAction(nameof(Index));
        }

        // POST: CategoryRates/Archive/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int id)
        {
            if (!CanModify) return Forbid();

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var rate = await _context.CategoryRates.FindAsync(id);
            if (rate == null) return NotFound();

            rate.IsArchived = true;
            await _context.SaveChangesAsync();

            await _audit.LogAsync(AuditAction.Archive, AuditModule.CategoryManagement,
                "CategoryRate", rate.Id.ToString(), rate.Category,
                $"Archived category '{rate.Category}' with daily rate \u20b1{rate.DailyRate:F2}.");

            TempData["Success"] = "Category archived successfully!";
            return RedirectToAction(nameof(Index));
        }

        // POST: CategoryRates/Restore/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            if (!CanModify) return Forbid();

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var rate = await _context.CategoryRates.FindAsync(id);
            if (rate == null) return NotFound();

            rate.IsArchived = false;
            await _context.SaveChangesAsync();

            await _audit.LogAsync(AuditAction.Update, AuditModule.CategoryManagement,
                "CategoryRate", rate.Id.ToString(), rate.Category,
                $"Restored category '{rate.Category}' from archive.");

            TempData["Success"] = "Category restored successfully!";
            return RedirectToAction(nameof(Index));
        }
    }
}
