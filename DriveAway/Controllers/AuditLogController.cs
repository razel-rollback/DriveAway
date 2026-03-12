using DriveAway.Data;
using DriveAway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Super Admin")]
    public class AuditLogController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AuditLogController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(
            string? userEmail,
            string? module,
            string? auditAction,
            DateTime? from,
            DateTime? to,
            int page = 1)
        {
            const int pageSize = 50;

            var query = _context.AuditLogs.AsQueryable();

            if (!string.IsNullOrWhiteSpace(userEmail))
                query = query.Where(l => l.UserEmail != null && l.UserEmail.Contains(userEmail));

            if (!string.IsNullOrWhiteSpace(module))
                query = query.Where(l => l.Module == module);

            if (!string.IsNullOrWhiteSpace(auditAction))
                query = query.Where(l => l.Action == auditAction);

            if (from.HasValue)
                query = query.Where(l => l.Timestamp >= from.Value.Date);

            if (to.HasValue)
                query = query.Where(l => l.Timestamp < to.Value.Date.AddDays(1));

            var totalCount = await query.CountAsync();

            var logs = await query
                .OrderByDescending(l => l.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var today = DateTime.UtcNow.Date;

            ViewBag.TotalAll     = await _context.AuditLogs.CountAsync();
            ViewBag.TotalToday   = await _context.AuditLogs.CountAsync(l => l.Timestamp >= today);
            ViewBag.TotalWeek    = await _context.AuditLogs.CountAsync(l => l.Timestamp >= today.AddDays(-7));
            ViewBag.UniqueUsers  = await _context.AuditLogs.Select(l => l.UserEmail).Distinct().CountAsync();

            ViewBag.Modules      = await _context.AuditLogs.Select(l => l.Module).Distinct().OrderBy(m => m).ToListAsync();
            ViewBag.Actions      = await _context.AuditLogs.Select(l => l.Action).Distinct().OrderBy(a => a).ToListAsync();

            ViewBag.FilterUserEmail = userEmail;
            ViewBag.FilterModule    = module;
            ViewBag.FilterAction    = auditAction;
            ViewBag.FilterFrom      = from?.ToString("yyyy-MM-dd");
            ViewBag.FilterTo        = to?.ToString("yyyy-MM-dd");

            ViewBag.Page       = page;
            ViewBag.PageSize   = pageSize;
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            return View(logs);
        }
    }
}
