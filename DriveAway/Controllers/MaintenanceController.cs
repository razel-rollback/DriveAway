using DriveAway.Data;
using DriveAway.Models;
using DriveAway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Admin,Super Admin,Business Owner")]
    public class MaintenanceController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _audit;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IEmailService _email;

        public MaintenanceController(ApplicationDbContext context, IAuditService audit, UserManager<IdentityUser> userManager, IEmailService email)
        {
            _context = context;
            _audit = audit;
            _userManager = userManager;
            _email = email;
        }

        private async Task<int?> GetCurrentUserBranchId()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return null;
            var ub = await _context.UserBranches.FirstOrDefaultAsync(u => u.UserId == user.Id);
            return ub?.BranchId;
        }

        // ─── Jobs List ─────────────────────────────────────────────────────
        public async Task<IActionResult> Jobs()
        {
            var query = _context.MaintenanceJobs
                .Include(j => j.Vehicle).ThenInclude(v => v.Branch)
                .Include(j => j.RentalContract)
                .Include(j => j.RepairParts)
                .AsQueryable();

            // Branch filter for Admin
            if (!User.IsInRole("Business Owner") && !User.IsInRole("Super Admin"))
            {
                var branchId = await GetCurrentUserBranchId();
                if (branchId.HasValue)
                    query = query.Where(j => j.Vehicle.BranchId == branchId.Value);
            }

            var jobs = await query.OrderByDescending(j => j.CreatedAt).ToListAsync();

            // Get mechanic users for assignment dropdown
            var mechanicUsers = await _userManager.GetUsersInRoleAsync("Mechanic");

            // Filter mechanics by branch if Admin
            if (!User.IsInRole("Business Owner") && !User.IsInRole("Super Admin"))
            {
                var branchId = await GetCurrentUserBranchId();
                if (branchId.HasValue)
                {
                    var branchUserIds = await _context.UserBranches
                        .Where(ub => ub.BranchId == branchId.Value)
                        .Select(ub => ub.UserId)
                        .ToListAsync();
                    mechanicUsers = mechanicUsers.Where(m => branchUserIds.Contains(m.Id)).ToList();
                }
            }

            ViewBag.Mechanics = mechanicUsers;
            return View(jobs);
        }

        // ─── Scan Fleet — Generate Maintenance Jobs ─────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScanFleet()
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var vehiclesQuery = _context.Vehicles
                .Include(v => v.Branch)
                .Where(v => v.Status != VehicleStatus.Retired);

            // Branch filter for Admin
            if (!User.IsInRole("Business Owner") && !User.IsInRole("Super Admin"))
            {
                var branchId = await GetCurrentUserBranchId();
                if (branchId.HasValue)
                    vehiclesQuery = vehiclesQuery.Where(v => v.BranchId == branchId.Value);
            }

            var vehicles = await vehiclesQuery.ToListAsync();

            // Get all existing pending/assigned maintenance jobs to avoid duplicates
            var existingJobs = await _context.MaintenanceJobs
                .Where(j => j.MaintenanceType != null &&
                       (j.JobStatus == MaintenanceJobStatus.Pending || j.JobStatus == MaintenanceJobStatus.Assigned || j.JobStatus == MaintenanceJobStatus.InProgress))
                .ToListAsync();

            int created = 0;
            var now = DateTime.UtcNow;
            var user = await _userManager.GetUserAsync(User);

            foreach (var v in vehicles)
            {
                var lastMileage = v.LastMaintenanceMileage ?? v.InitialMileage;
                var lastDate = v.LastMaintenanceDate ?? v.AcquisitionDate;

                var vehicleExistingJobs = existingJobs.Where(j => j.VehicleId == v.Id).ToList();

                // Minor Service every 5,000 km
                var nextMinorMileage = ((lastMileage / 5000) + 1) * 5000;
                if (v.CurrentMileage >= nextMinorMileage - 500 &&
                    !vehicleExistingJobs.Any(j => j.MaintenanceType == MaintenanceType.MinorService && j.ScheduledAtMileage == nextMinorMileage))
                {
                    _context.MaintenanceJobs.Add(new MaintenanceJob
                    {
                        VehicleId = v.Id,
                        MaintenanceType = MaintenanceType.MinorService,
                        ScheduledAtMileage = nextMinorMileage,
                        DamageSeverity = DamageSeverity.None,
                        DamageDescription = $"Minor Service at {nextMinorMileage:N0} km",
                        JobStatus = MaintenanceJobStatus.Pending,
                        CreatedAt = now,
                        CreatedByEmail = user?.Email
                    });
                    created++;
                }

                // Major Service every 10,000 km
                var nextMajorMileage = ((lastMileage / 10000) + 1) * 10000;
                if (v.CurrentMileage >= nextMajorMileage - 1000 &&
                    !vehicleExistingJobs.Any(j => j.MaintenanceType == MaintenanceType.MajorService && j.ScheduledAtMileage == nextMajorMileage))
                {
                    _context.MaintenanceJobs.Add(new MaintenanceJob
                    {
                        VehicleId = v.Id,
                        MaintenanceType = MaintenanceType.MajorService,
                        ScheduledAtMileage = nextMajorMileage,
                        DamageSeverity = DamageSeverity.None,
                        DamageDescription = $"Major Service at {nextMajorMileage:N0} km",
                        JobStatus = MaintenanceJobStatus.Pending,
                        CreatedAt = now,
                        CreatedByEmail = user?.Email
                    });
                    created++;
                }

                // General Inspection every 6 months
                var nextInspection = lastDate.AddMonths(6);
                if (nextInspection <= now.AddDays(30) &&
                    !vehicleExistingJobs.Any(j => j.MaintenanceType == MaintenanceType.GeneralInspection))
                {
                    _context.MaintenanceJobs.Add(new MaintenanceJob
                    {
                        VehicleId = v.Id,
                        MaintenanceType = MaintenanceType.GeneralInspection,
                        ScheduledAtMileage = null,
                        DamageSeverity = DamageSeverity.None,
                        DamageDescription = "6-Month General Inspection",
                        JobStatus = MaintenanceJobStatus.Pending,
                        CreatedAt = now,
                        CreatedByEmail = user?.Email
                    });
                    created++;
                }

                // Set vehicle to UnderMaintenance if any new job was created for it
                if (created > 0 && v.Status == VehicleStatus.Available)
                {
                    v.Status = VehicleStatus.UnderMaintenance;

                    _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
                    {
                        VehicleId = v.Id,
                        EventType = LifecycleEventType.MaintenanceScheduled,
                        EventDate = now,
                        Notes = $"Vehicle placed under maintenance via fleet scan.",
                        Mileage = v.CurrentMileage
                    });
                }
            }

            if (created > 0)
            {
                await _context.SaveChangesAsync();

                await _audit.LogAsync(
                    AuditAction.CreateMaintenanceJob,
                    AuditModule.MaintenanceJobs,
                    entityType: "MaintenanceJob",
                    entityId: null,
                    entityName: null,
                    details: $"Fleet scan generated {created} maintenance job(s) for {vehicles.Count} vehicle(s).",
                    userEmailOverride: null);
            }

            TempData["Success"] = $"Fleet scan complete. {created} maintenance job(s) created.";
            return RedirectToAction(nameof(Jobs));
        }

        // ─── Assign Mechanic ───────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignMechanic(int jobId, string mechanicId)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var job = await _context.MaintenanceJobs
                .Include(j => j.Vehicle)
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (job == null) return NotFound();

            var mechanic = await _userManager.FindByIdAsync(mechanicId);
            if (mechanic == null) return NotFound();

            job.AssignedMechanicId = mechanic.Id;
            job.AssignedMechanicEmail = mechanic.Email;
            job.AssignedAt = DateTime.UtcNow;
            job.JobStatus = MaintenanceJobStatus.Assigned;

            _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
            {
                VehicleId = job.VehicleId,
                EventType = LifecycleEventType.RepairAssigned,
                EventDate = DateTime.UtcNow,
                Notes = $"Repair assigned to {mechanic.Email}. {(job.MaintenanceType.HasValue ? $"Type: {job.MaintenanceType}." : $"Damage: {job.DamageSeverity}.")}",
                Mileage = job.Vehicle.CurrentMileage
            });

            await _context.SaveChangesAsync();

            await _audit.LogAsync(
                AuditAction.AssignMechanic,
                AuditModule.MaintenanceJobs,
                entityType: "MaintenanceJob",
                entityId: job.Id.ToString(),
                entityName: $"{job.Vehicle.PlateNumber}",
                details: $"Assigned mechanic {mechanic.Email} to {(job.MaintenanceType.HasValue ? job.MaintenanceType.ToString() : $"{job.DamageSeverity} damage repair")} for {job.Vehicle.PlateNumber}.");

            TempData["Success"] = $"Mechanic {mechanic.Email} assigned to {job.Vehicle.PlateNumber}.";

            // Send assignment notification email to mechanic
            var assignJobType = job.MaintenanceType.HasValue ? job.MaintenanceType.ToString() : $"{job.DamageSeverity} Damage Repair";
            var assignEmailBody = $@"
                <h2>Maintenance Job Assignment</h2>
                <p>Hello,</p>
                <p>You have been assigned a new maintenance job:</p>
                <table style='border-collapse:collapse;'>
                    <tr><td style='padding:4px 12px;'><strong>Vehicle:</strong></td><td>{job.Vehicle.Year} {job.Vehicle.Make} {job.Vehicle.Model} ({job.Vehicle.PlateNumber})</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Job Type:</strong></td><td>{assignJobType}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Description:</strong></td><td>{job.DamageDescription ?? "N/A"}</td></tr>
                </table>
                <p>Please log in to your DriveAway dashboard to begin work.</p>";
            try { await _email.SendEmailAsync(mechanic.Email, $"New Maintenance Job Assigned — {job.Vehicle.PlateNumber}", assignEmailBody); }
            catch { /* logged by SmtpEmailService */ }

            return RedirectToAction(nameof(Jobs));
        }
    }
}
