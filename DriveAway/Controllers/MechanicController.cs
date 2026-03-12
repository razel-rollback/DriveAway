using DriveAway.Data;
using DriveAway.Models;
using DriveAway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Mechanic")]
    public class MechanicController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _audit;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IEmailService _email;

        public MechanicController(ApplicationDbContext context, IAuditService audit, UserManager<IdentityUser> userManager, IEmailService email)
        {
            _context = context;
            _audit = audit;
            _userManager = userManager;
            _email = email;
        }

        private string GetCurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        // ─── Dashboard ─────────────────────────────────────────────────────
        public async Task<IActionResult> Dashboard()
        {
            var userId = GetCurrentUserId();
            var jobs = await _context.MaintenanceJobs
                .Include(j => j.Vehicle).ThenInclude(v => v.Branch)
                .Include(j => j.RepairParts)
                .Where(j => j.AssignedMechanicId == userId)
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync();

            ViewBag.Assigned = jobs.Count(j => j.JobStatus == MaintenanceJobStatus.Assigned);
            ViewBag.InProgress = jobs.Count(j => j.JobStatus == MaintenanceJobStatus.InProgress);
            ViewBag.Completed = jobs.Count(j => j.JobStatus == MaintenanceJobStatus.Completed);
            ViewBag.Total = jobs.Count;

            return View(jobs);
        }

        // ─── Start Repair ──────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartRepair(int id)
        {
            var userId = GetCurrentUserId();
            var job = await _context.MaintenanceJobs
                .Include(j => j.Vehicle)
                .FirstOrDefaultAsync(j => j.Id == id && j.AssignedMechanicId == userId);

            if (job == null) return NotFound();

            job.JobStatus = MaintenanceJobStatus.InProgress;

            // Set vehicle to UnderMaintenance if not already
            if (job.Vehicle.Status != VehicleStatus.UnderMaintenance)
            {
                job.Vehicle.Status = VehicleStatus.UnderMaintenance;
            }

            await _context.SaveChangesAsync();

            await _audit.LogAsync(
                AuditAction.StartRepair,
                AuditModule.MaintenanceJobs,
                entityType: "MaintenanceJob",
                entityId: job.Id.ToString(),
                entityName: $"{job.Vehicle.PlateNumber}",
                details: $"Mechanic started repair on {job.Vehicle.PlateNumber}. Damage: {job.DamageSeverity}.");

            TempData["Success"] = $"Repair started for {job.Vehicle.PlateNumber}.";

            // Notify the admin/staff who created the job
            var mechanicEmailStart = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
            var emailBodyStart = $@"
                <h2>Repair Started</h2>
                <p>Mechanic <strong>{mechanicEmailStart}</strong> has started repair on vehicle <strong>{job.Vehicle.PlateNumber}</strong>.</p>
                <table style='border-collapse:collapse;'>
                    <tr><td style='padding:4px 12px;'><strong>Vehicle:</strong></td><td>{job.Vehicle.Year} {job.Vehicle.Make} {job.Vehicle.Model}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Severity:</strong></td><td>{job.DamageSeverity}</td></tr>
                </table>";
            try { await _email.SendEmailAsync(job.CreatedByEmail, $"Repair Started — {job.Vehicle.PlateNumber}", emailBodyStart); }
            catch { /* logged by SmtpEmailService */ }

            return RedirectToAction(nameof(Dashboard));
        }

        // ─── Complete Repair (GET) ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> CompleteRepair(int id)
        {
            var userId = GetCurrentUserId();
            var job = await _context.MaintenanceJobs
                .Include(j => j.Vehicle)
                .Include(j => j.RepairParts)
                .FirstOrDefaultAsync(j => j.Id == id && j.AssignedMechanicId == userId);

            if (job == null) return NotFound();

            return View(job);
        }

        // ─── Complete Repair (POST) ────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteRepair(int id, string? repairNotes,
            string[]? partName, string[]? partQuantity, decimal[]? partUnitCost, decimal[]? partTotalCost)
        {
            var userId = GetCurrentUserId();
            var job = await _context.MaintenanceJobs
                .Include(j => j.Vehicle)
                .Include(j => j.RepairParts)
                .FirstOrDefaultAsync(j => j.Id == id && j.AssignedMechanicId == userId);

            if (job == null) return NotFound();

            job.RepairNotes = repairNotes;
            job.CompletedAt = DateTime.UtcNow;
            job.JobStatus = MaintenanceJobStatus.Completed;

            // Save parts
            decimal totalRepairCost = 0;
            if (partName != null)
            {
                for (int i = 0; i < partName.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(partName[i]))
                    {
                        var cost = (partTotalCost != null && i < partTotalCost.Length) ? partTotalCost[i] : 0;
                        totalRepairCost += cost;

                        _context.RepairParts.Add(new RepairPart
                        {
                            MaintenanceJobId = job.Id,
                            PartName = partName[i],
                            Quantity = (partQuantity != null && i < partQuantity.Length) ? partQuantity[i] : "1",
                            UnitCost = (partUnitCost != null && i < partUnitCost.Length) ? partUnitCost[i] : 0,
                            TotalCost = cost
                        });
                    }
                }
            }
            job.RepairCost = totalRepairCost;

            // Return vehicle to Available
            job.Vehicle.Status = VehicleStatus.Available;
            job.Vehicle.LastMaintenanceDate = DateTime.UtcNow;
            job.Vehicle.LastMaintenanceMileage = job.Vehicle.CurrentMileage;

            _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
            {
                VehicleId = job.VehicleId,
                EventType = LifecycleEventType.RepairCompleted,
                EventDate = DateTime.UtcNow,
                Notes = $"Repair completed by {User.Identity?.Name}. Cost: ₱{totalRepairCost:N2}. {repairNotes}",
                Mileage = job.Vehicle.CurrentMileage
            });

            await _context.SaveChangesAsync();

            await _audit.LogAsync(
                AuditAction.RepairComplete,
                AuditModule.MaintenanceJobs,
                entityType: "MaintenanceJob",
                entityId: job.Id.ToString(),
                entityName: $"{job.Vehicle.PlateNumber}",
                details: $"Repair completed. Cost: ₱{totalRepairCost:N2}. Vehicle returned to Available. Notes: {repairNotes}");

            TempData["Success"] = $"Repair completed for {job.Vehicle.PlateNumber}. Vehicle is now Available.";

            // Notify the admin/staff who created the job
            var mechanicEmailComplete = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
            var emailBodyComplete = $@"
                <h2>Repair Completed</h2>
                <p>Mechanic <strong>{mechanicEmailComplete}</strong> has completed the repair on vehicle <strong>{job.Vehicle.PlateNumber}</strong>.</p>
                <table style='border-collapse:collapse;'>
                    <tr><td style='padding:4px 12px;'><strong>Vehicle:</strong></td><td>{job.Vehicle.Year} {job.Vehicle.Make} {job.Vehicle.Model}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Repair Cost:</strong></td><td>₱{totalRepairCost:N2}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Notes:</strong></td><td>{repairNotes ?? "N/A"}</td></tr>
                </table>
                <p>The vehicle has been returned to <strong>Available</strong> status.</p>";
            try { await _email.SendEmailAsync(job.CreatedByEmail, $"Repair Completed — {job.Vehicle.PlateNumber}", emailBodyComplete); }
            catch { /* logged by SmtpEmailService */ }

            return RedirectToAction(nameof(Dashboard));
        }

        // ─── History ───────────────────────────────────────────────────────
        public async Task<IActionResult> History()
        {
            var userId = GetCurrentUserId();
            var jobs = await _context.MaintenanceJobs
                .Include(j => j.Vehicle).ThenInclude(v => v.Branch)
                .Include(j => j.RepairParts)
                .Where(j => j.AssignedMechanicId == userId && j.JobStatus == MaintenanceJobStatus.Completed)
                .OrderByDescending(j => j.CompletedAt)
                .ToListAsync();

            return View(jobs);
        }
    }
}
