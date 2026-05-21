using DriveAway.Data;
using DriveAway.Models;
using DriveAway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Business Owner")]
    public class BusinessOwnerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _audit;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IEmailService _email;

        public BusinessOwnerController(ApplicationDbContext context, IAuditService audit, UserManager<IdentityUser> userManager, IEmailService email)
        {
            _context = context;
            _audit = audit;
            _userManager = userManager;
            _email = email;
        }

        // ─── DASHBOARD ─────────────────────────────────────────────────
        public async Task<IActionResult> Dashboard(string filter = "month", DateTime? startDate = null, DateTime? endDate = null)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var today = DateTime.UtcNow.Date;

            // ── Determine filter date range ──────────────────────
            DateTime filterStart, filterEnd;
            switch (filter?.ToLower())
            {
                case "day":
                    filterStart = today;
                    filterEnd = today.AddDays(1);
                    break;
                case "week":
                    var dayOfWeek = (int)today.DayOfWeek;
                    filterStart = today.AddDays(-dayOfWeek);
                    filterEnd = filterStart.AddDays(7);
                    break;
                case "year":
                    filterStart = new DateTime(today.Year, 1, 1);
                    filterEnd = filterStart.AddYears(1);
                    break;
                case "custom" when startDate.HasValue && endDate.HasValue:
                    filterStart = startDate.Value.Date;
                    filterEnd = endDate.Value.Date.AddDays(1);
                    break;
                default:
                    filter = "month";
                    filterStart = new DateTime(today.Year, today.Month, 1);
                    filterEnd = filterStart.AddMonths(1);
                    break;
            }

            ViewBag.CurrentFilter = filter;
            ViewBag.FilterStart = filterStart.ToString("yyyy-MM-dd");
            ViewBag.FilterEnd = filterEnd.AddDays(-1).ToString("yyyy-MM-dd");

            string filterLabel = filter switch
            {
                "day" => "Today",
                "week" => "This Week",
                "year" => "This Year",
                "custom" => $"{filterStart:MMM dd} – {filterEnd.AddDays(-1):MMM dd, yyyy}",
                _ => "This Month"
            };
            ViewBag.FilterLabel = filterLabel;

            // ── Fleet Metrics (all branches) ─────────────────────
            var vehicles = await _context.Vehicles.ToListAsync();
            ViewBag.TotalVehicles = vehicles.Count;
            ViewBag.AvailableVehicles = vehicles.Count(v => v.Status == VehicleStatus.Available);
            ViewBag.RentedVehicles = vehicles.Count(v => v.Status == VehicleStatus.Rented);
            ViewBag.UnderMaintenance = vehicles.Count(v => v.Status == VehicleStatus.UnderMaintenance);
            ViewBag.OutOfService = vehicles.Count(v => v.Status == VehicleStatus.OutOfService);
            ViewBag.RetiredVehicles = vehicles.Count(v => v.Status == VehicleStatus.Retired);
            ViewBag.TotalFleetValue = vehicles.Where(v => v.Status != VehicleStatus.Retired).Sum(v => v.CurrentBookValue);

            // ── Branch Metrics ───────────────────────────────────
            var branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();
            ViewBag.TotalBranches = branches.Count;

            var branchData = branches.Select(b => new
            {
                Name = b.Name,
                City = b.City,
                FleetSize = vehicles.Count(v => v.BranchId == b.Id),
                ActiveRentals = vehicles.Count(v => v.BranchId == b.Id && v.Status == VehicleStatus.Rented),
                FleetValue = vehicles.Where(v => v.BranchId == b.Id && v.Status != VehicleStatus.Retired).Sum(v => v.CurrentBookValue)
            }).ToList();
            ViewBag.BranchData = branchData;

            // ── Rental Metrics (filtered) ────────────────────────
            var allContracts = await _context.RentalContracts.ToListAsync();
            var filteredContracts = allContracts.Where(c => c.CreatedAt >= filterStart && c.CreatedAt < filterEnd).ToList();
            ViewBag.ActiveRentals = allContracts.Count(c => c.RentalStatus == RentalStatus.Active);
            ViewBag.OverdueRentals = allContracts.Count(c => c.RentalStatus == RentalStatus.Active && c.RentalEnd.Date < today);
            ViewBag.CompletedRentals = filteredContracts.Count(c => c.RentalStatus == RentalStatus.Completed);
            ViewBag.TotalContracts = allContracts.Count;

            // ── Revenue (filtered) ───────────────────────────────
            var periodRevenue = filteredContracts.Sum(c => c.FinalFee ?? c.TotalFee);
            var totalRevenue = allContracts
                .Where(c => c.RentalStatus == RentalStatus.Completed || c.RentalStatus == RentalStatus.Active)
                .Sum(c => c.FinalFee ?? c.TotalFee);
            ViewBag.PeriodRevenue = periodRevenue;
            ViewBag.TotalRevenue = totalRevenue;

            // ── Payments (filtered) ──────────────────────────────
            var payments = await _context.Payments.ToListAsync();
            var filteredPayments = payments.Where(p => p.CreatedAt >= filterStart && p.CreatedAt < filterEnd).ToList();
            ViewBag.TotalPaymentsCollected = filteredPayments.Where(p => p.PaymentStatus == PaymentStatus.Paid).Sum(p => p.Amount);
            ViewBag.PendingPayments = filteredPayments.Where(p => p.PaymentStatus == PaymentStatus.Pending).Sum(p => p.Amount);

            // ── Maintenance Metrics (filtered) ───────────────────
            var maintenanceJobs = await _context.MaintenanceJobs.ToListAsync();
            var filteredMaintenance = maintenanceJobs.Where(j => j.CreatedAt >= filterStart && j.CreatedAt < filterEnd).ToList();
            ViewBag.PendingMaintenance = maintenanceJobs.Count(j => j.JobStatus == MaintenanceJobStatus.Pending);
            ViewBag.InProgressMaintenance = maintenanceJobs.Count(j => j.JobStatus == MaintenanceJobStatus.InProgress);
            ViewBag.CompletedMaintenance = filteredMaintenance.Count(j => j.JobStatus == MaintenanceJobStatus.Completed);
            ViewBag.TotalMaintenanceCost = filteredMaintenance.Where(j => j.RepairCost.HasValue).Sum(j => j.RepairCost!.Value);

            // ── Vehicle Status Distribution (doughnut chart) ─────
            ViewBag.VehicleStatusLabels = new[] { "Available", "Rented", "Maintenance", "Out of Service", "Retired" };
            ViewBag.VehicleStatusCounts = new[]
            {
                vehicles.Count(v => v.Status == VehicleStatus.Available),
                vehicles.Count(v => v.Status == VehicleStatus.Rented),
                vehicles.Count(v => v.Status == VehicleStatus.UnderMaintenance),
                vehicles.Count(v => v.Status == VehicleStatus.OutOfService),
                vehicles.Count(v => v.Status == VehicleStatus.Retired)
            };

            // ── Revenue & Rental Chart Data (adapts to filter) ───
            var revenueByPeriod = new List<object>();
            var rentalsByPeriod = new List<object>();
            var totalDays = (filterEnd - filterStart).Days;

            if (totalDays <= 1)
            {
                for (int h = 0; h < 24; h += 4)
                {
                    var hStart = filterStart.AddHours(h);
                    var hEnd = filterStart.AddHours(h + 4);
                    var rev = allContracts.Where(c => c.CreatedAt >= hStart && c.CreatedAt < hEnd).Sum(c => c.FinalFee ?? c.TotalFee);
                    var cnt = allContracts.Count(c => c.CreatedAt >= hStart && c.CreatedAt < hEnd);
                    revenueByPeriod.Add(new { Label = hStart.ToString("h tt"), Revenue = rev });
                    rentalsByPeriod.Add(new { Label = hStart.ToString("h tt"), Count = cnt });
                }
            }
            else if (totalDays <= 14)
            {
                for (var d = filterStart; d < filterEnd; d = d.AddDays(1))
                {
                    var dEnd = d.AddDays(1);
                    var rev = allContracts.Where(c => c.CreatedAt >= d && c.CreatedAt < dEnd).Sum(c => c.FinalFee ?? c.TotalFee);
                    var cnt = allContracts.Count(c => c.CreatedAt >= d && c.CreatedAt < dEnd);
                    revenueByPeriod.Add(new { Label = d.ToString("MMM dd"), Revenue = rev });
                    rentalsByPeriod.Add(new { Label = d.ToString("MMM dd"), Count = cnt });
                }
            }
            else if (totalDays <= 366)
            {
                var cursor = new DateTime(filterStart.Year, filterStart.Month, 1);
                if (filter == "month")
                {
                    cursor = today.AddMonths(-5);
                    cursor = new DateTime(cursor.Year, cursor.Month, 1);
                    var chartEnd = new DateTime(today.Year, today.Month, 1).AddMonths(1);
                    while (cursor < chartEnd)
                    {
                        var mEnd = cursor.AddMonths(1);
                        var rev = allContracts.Where(c => c.CreatedAt >= cursor && c.CreatedAt < mEnd).Sum(c => c.FinalFee ?? c.TotalFee);
                        var cnt = allContracts.Count(c => c.CreatedAt >= cursor && c.CreatedAt < mEnd);
                        revenueByPeriod.Add(new { Label = cursor.ToString("MMM yyyy"), Revenue = rev });
                        rentalsByPeriod.Add(new { Label = cursor.ToString("MMM yyyy"), Count = cnt });
                        cursor = mEnd;
                    }
                }
                else
                {
                    while (cursor < filterEnd)
                    {
                        var mEnd = cursor.AddMonths(1);
                        var rev = allContracts.Where(c => c.CreatedAt >= cursor && c.CreatedAt < mEnd).Sum(c => c.FinalFee ?? c.TotalFee);
                        var cnt = allContracts.Count(c => c.CreatedAt >= cursor && c.CreatedAt < mEnd);
                        revenueByPeriod.Add(new { Label = cursor.ToString("MMM yyyy"), Revenue = rev });
                        rentalsByPeriod.Add(new { Label = cursor.ToString("MMM yyyy"), Count = cnt });
                        cursor = mEnd;
                    }
                }
            }
            else
            {
                for (int q = 1; q <= 4; q++)
                {
                    var qStart = new DateTime(filterStart.Year, (q - 1) * 3 + 1, 1);
                    var qEnd = qStart.AddMonths(3);
                    var rev = allContracts.Where(c => c.CreatedAt >= qStart && c.CreatedAt < qEnd).Sum(c => c.FinalFee ?? c.TotalFee);
                    var cnt = allContracts.Count(c => c.CreatedAt >= qStart && c.CreatedAt < qEnd);
                    revenueByPeriod.Add(new { Label = $"Q{q} {filterStart.Year}", Revenue = rev });
                    rentalsByPeriod.Add(new { Label = $"Q{q} {filterStart.Year}", Count = cnt });
                }
            }

            ViewBag.RevenueByPeriod = revenueByPeriod;
            ViewBag.RentalsByPeriod = rentalsByPeriod;

            // ── Top Rented Vehicles ──────────────────────────────
            var topVehicles = allContracts
                .GroupBy(c => c.VehicleId)
                .Select(g => new
                {
                    VehicleId = g.Key,
                    RentalCount = g.Count(),
                    Revenue = g.Sum(c => c.FinalFee ?? c.TotalFee)
                })
                .OrderByDescending(x => x.RentalCount)
                .Take(5)
                .Select(x => new
                {
                    Vehicle = vehicles.FirstOrDefault(v => v.Id == x.VehicleId),
                    x.RentalCount,
                    x.Revenue
                })
                .Where(x => x.Vehicle != null)
                .ToList<object>();
            ViewBag.TopVehicles = topVehicles;

            // ── Recent Audit Logs ────────────────────────────────
            var recentLogs = await _context.AuditLogs
                .OrderByDescending(a => a.Timestamp)
                .Take(8)
                .ToListAsync();
            ViewBag.RecentAuditLogs = recentLogs;

            return View();
        }

        // ─── FLEET MANAGEMENT (Unified Unassigned + Transfer) ───────────
        [HttpGet]
        public async Task<IActionResult> FleetManagement()
        {
            var vehicles = await _context.Vehicles.Include(v => v.Branch).ToListAsync();
            var branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();

            ViewBag.Vehicles = vehicles;
            ViewBag.Branches = branches;
            ViewBag.UnassignedVehicles = vehicles.Where(v => v.BranchId == null && v.Status != VehicleStatus.Retired).ToList();

            return View();
        }

        // ─── ASSIGN VEHICLE TO BRANCH ───────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignVehicleToBranch(int vehicleId, int branchId)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var vehicle = await _context.Vehicles.FindAsync(vehicleId);
            var branch = await _context.Branches.FindAsync(branchId);
            if (vehicle == null || branch == null) return NotFound();

            vehicle.BranchId = branchId;

            _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
            {
                VehicleId = vehicle.Id,
                EventType = LifecycleEventType.BranchAssigned,
                EventDate = DateTime.UtcNow,
                Notes = $"Vehicle assigned to branch: {branch.Name}.",
                Mileage = vehicle.CurrentMileage
            });

            await _context.SaveChangesAsync();

            await _audit.LogAsync(AuditAction.Assign, AuditModule.BranchManagement, "Vehicle",
                vehicle.Id.ToString(),
                $"{vehicle.Year} {vehicle.Make} {vehicle.Model} ({vehicle.PlateNumber})",
                $"Assigned to branch: {branch.Name} (ID: {branch.Id}).");

            TempData["Success"] = $"{vehicle.PlateNumber} assigned to {branch.Name}.";
            return RedirectToAction(nameof(FleetManagement));
        }

        // ─── TRANSFER VEHICLE BETWEEN BRANCHES ─────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TransferVehicle(int vehicleId, int toBranchId)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var vehicle = await _context.Vehicles.Include(v => v.Branch).FirstOrDefaultAsync(v => v.Id == vehicleId);
            var toBranch = await _context.Branches.FindAsync(toBranchId);
            if (vehicle == null || toBranch == null) return NotFound();

            var fromBranchName = vehicle.Branch?.Name ?? "Unassigned";
            vehicle.BranchId = toBranchId;

            _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
            {
                VehicleId = vehicle.Id,
                EventType = LifecycleEventType.BranchTransferred,
                EventDate = DateTime.UtcNow,
                Notes = $"Transferred from {fromBranchName} to {toBranch.Name}.",
                Mileage = vehicle.CurrentMileage
            });

            await _context.SaveChangesAsync();

            await _audit.LogAsync(AuditAction.Transfer, AuditModule.BranchManagement, "Vehicle",
                vehicle.Id.ToString(),
                $"{vehicle.Year} {vehicle.Make} {vehicle.Model} ({vehicle.PlateNumber})",
                $"Transferred from {fromBranchName} to {toBranch.Name}.");

            TempData["Success"] = $"{vehicle.PlateNumber} transferred to {toBranch.Name}.";
            return RedirectToAction(nameof(FleetManagement));
        }

        // ─── APPROVE DISPOSAL ───────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveDisposal(int id)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var request = await _context.DisposalRequests.Include(d => d.Vehicle).FirstOrDefaultAsync(d => d.Id == id);
            if (request == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            request.Status = DisposalRequestStatus.Approved;
            request.ReviewedByUserId = user?.Id;
            request.ReviewedByEmail = user?.Email;
            request.ReviewedAt = DateTime.UtcNow;

            // Update vehicle
            var vehicle = request.Vehicle;
            vehicle.Status = VehicleStatus.Retired;
            vehicle.DisposalDate = DateTime.UtcNow;
            vehicle.DisposalValue = request.RecommendedDisposalValue;

            _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
            {
                VehicleId = vehicle.Id,
                EventType = LifecycleEventType.DisposalApproved,
                EventDate = DateTime.UtcNow,
                Notes = $"Disposal approved. Reason: {request.Reason}. Disposal value: ₱{request.RecommendedDisposalValue:N2}.",
                Mileage = vehicle.CurrentMileage
            });

            await _context.SaveChangesAsync();

            await _audit.LogAsync(AuditAction.DisposalApprove, AuditModule.Disposal, "Vehicle",
                vehicle.Id.ToString(),
                $"{vehicle.Year} {vehicle.Make} {vehicle.Model} ({vehicle.PlateNumber})",
                $"Disposal approved. Value: ₱{request.RecommendedDisposalValue:N2}. Reason: {request.Reason}.");

            TempData["Success"] = $"Disposal approved for {vehicle.PlateNumber}. Vehicle marked as Retired.";

            // Notify the requester
            var approveEmailBody = $@"
                <h2>Disposal Request Approved</h2>
                <p>Your disposal request for <strong>{vehicle.PlateNumber}</strong> ({vehicle.Year} {vehicle.Make} {vehicle.Model}) has been <strong>approved</strong>.</p>
                <table style='border-collapse:collapse;'>
                    <tr><td style='padding:4px 12px;'><strong>Disposal Value:</strong></td><td>₱{request.RecommendedDisposalValue:N2}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Reviewed By:</strong></td><td>{request.ReviewedByEmail}</td></tr>
                </table>
                <p>The vehicle has been marked as <strong>Retired</strong>.</p>";
            try { await _email.SendEmailAsync(request.RequestedByEmail, $"Disposal Approved — {vehicle.PlateNumber}", approveEmailBody); }
            catch { /* logged by SmtpEmailService */ }

            return RedirectToAction(nameof(Dashboard));
        }

        // ─── REJECT DISPOSAL ────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectDisposal(int id, string? reviewNotes)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var request = await _context.DisposalRequests.Include(d => d.Vehicle).FirstOrDefaultAsync(d => d.Id == id);
            if (request == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            request.Status = DisposalRequestStatus.Rejected;
            request.ReviewedByUserId = user?.Id;
            request.ReviewedByEmail = user?.Email;
            request.ReviewedAt = DateTime.UtcNow;
            request.ReviewNotes = reviewNotes;

            _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
            {
                VehicleId = request.Vehicle.Id,
                EventType = LifecycleEventType.DisposalRejected,
                EventDate = DateTime.UtcNow,
                Notes = $"Disposal rejected. Notes: {reviewNotes ?? "N/A"}.",
                Mileage = request.Vehicle.CurrentMileage
            });

            await _context.SaveChangesAsync();

            await _audit.LogAsync(AuditAction.DisposalReject, AuditModule.Disposal, "Vehicle",
                request.Vehicle.Id.ToString(),
                $"{request.Vehicle.Year} {request.Vehicle.Make} {request.Vehicle.Model} ({request.Vehicle.PlateNumber})",
                $"Disposal rejected. Notes: {reviewNotes ?? "N/A"}.");

            TempData["Success"] = $"Disposal rejected for {request.Vehicle.PlateNumber}.";

            // Notify the requester
            var rejectEmailBody = $@"
                <h2>Disposal Request Rejected</h2>
                <p>Your disposal request for <strong>{request.Vehicle.PlateNumber}</strong> ({request.Vehicle.Year} {request.Vehicle.Make} {request.Vehicle.Model}) has been <strong>rejected</strong>.</p>
                <table style='border-collapse:collapse;'>
                    <tr><td style='padding:4px 12px;'><strong>Reviewed By:</strong></td><td>{request.ReviewedByEmail}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Notes:</strong></td><td>{reviewNotes ?? "N/A"}</td></tr>
                </table>";
            try { await _email.SendEmailAsync(request.RequestedByEmail, $"Disposal Rejected — {request.Vehicle.PlateNumber}", rejectEmailBody); }
            catch { /* logged by SmtpEmailService */ }

            return RedirectToAction(nameof(Dashboard));
        }
    }
}
