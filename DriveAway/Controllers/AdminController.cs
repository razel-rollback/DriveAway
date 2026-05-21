using DriveAway.Data;
using DriveAway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Super Admin,Admin,Business Owner")]
    public class AdminController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ApplicationDbContext _context;

        public AdminController(UserManager<IdentityUser> userManager, ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public async Task<IActionResult> Dashboard(string filter = "month", DateTime? startDate = null, DateTime? endDate = null)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var today = DateTime.UtcNow.Date;

            // ── Resolve the admin's assigned branch ──────────────
            var currentUser = await _userManager.GetUserAsync(User);
            var userBranch = await _context.UserBranches
                .Include(ub => ub.Branch)
                .FirstOrDefaultAsync(ub => ub.UserId == currentUser!.Id);

            var branch = userBranch?.Branch;
            var branchId = branch?.Id;

            ViewBag.BranchName = branch?.Name ?? "Unassigned";
            ViewBag.BranchCity = branch?.City ?? "";
            ViewBag.BranchContact = branch?.ContactNumber ?? "";

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
                default: // month
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

            // ── Fleet Metrics (branch-scoped) ────────────────────
            var vehicles = branchId.HasValue
                ? await _context.Vehicles.Where(v => v.BranchId == branchId).ToListAsync()
                : new List<Vehicle>();

            ViewBag.TotalVehicles = vehicles.Count;
            ViewBag.AvailableVehicles = vehicles.Count(v => v.Status == VehicleStatus.Available);
            ViewBag.RentedVehicles = vehicles.Count(v => v.Status == VehicleStatus.Rented);
            ViewBag.UnderMaintenance = vehicles.Count(v => v.Status == VehicleStatus.UnderMaintenance);
            ViewBag.OutOfService = vehicles.Count(v => v.Status == VehicleStatus.OutOfService);
            ViewBag.RetiredVehicles = vehicles.Count(v => v.Status == VehicleStatus.Retired);
            ViewBag.TotalFleetValue = vehicles.Where(v => v.Status != VehicleStatus.Retired).Sum(v => v.CurrentBookValue);

            // ── Rental Metrics (branch-scoped, filtered) ─────────
            var vehicleIds = vehicles.Select(v => v.Id).ToHashSet();
            var allContracts = vehicleIds.Any()
                ? await _context.RentalContracts.Where(c => vehicleIds.Contains(c.VehicleId)).ToListAsync()
                : new List<RentalContract>();

            var filteredContracts = allContracts.Where(c => c.CreatedAt >= filterStart && c.CreatedAt < filterEnd).ToList();
            ViewBag.ActiveRentals = allContracts.Count(c => c.RentalStatus == RentalStatus.Active);
            ViewBag.OverdueRentals = allContracts.Count(c => c.RentalStatus == RentalStatus.Active && c.RentalEnd.Date < today);
            ViewBag.CompletedRentals = filteredContracts.Count(c => c.RentalStatus == RentalStatus.Completed);
            ViewBag.CancelledRentals = filteredContracts.Count(c => c.RentalStatus == RentalStatus.Cancelled);
            ViewBag.TotalContracts = allContracts.Count;

            // ── Revenue (filtered) ───────────────────────────────
            var periodRevenue = filteredContracts.Sum(c => c.FinalFee ?? c.TotalFee);
            var totalRevenue = allContracts
                .Where(c => c.RentalStatus == RentalStatus.Completed || c.RentalStatus == RentalStatus.Active)
                .Sum(c => c.FinalFee ?? c.TotalFee);
            ViewBag.PeriodRevenue = periodRevenue;
            ViewBag.TotalRevenue = totalRevenue;

            // ── Payments (filtered) ──────────────────────────────
            var contractIds = allContracts.Select(c => c.Id).ToHashSet();
            var allPayments = contractIds.Any()
                ? await _context.Payments.Where(p => contractIds.Contains(p.RentalContractId)).ToListAsync()
                : new List<Payment>();

            var filteredPayments = allPayments.Where(p => p.CreatedAt >= filterStart && p.CreatedAt < filterEnd).ToList();
            ViewBag.TotalPaymentsCollected = filteredPayments.Where(p => p.PaymentStatus == PaymentStatus.Paid).Sum(p => p.Amount);
            ViewBag.PendingPayments = filteredPayments.Where(p => p.PaymentStatus == PaymentStatus.Pending).Sum(p => p.Amount);

            // ── Maintenance Metrics (branch-scoped, filtered) ────
            var allMaintenance = vehicleIds.Any()
                ? await _context.MaintenanceJobs.Where(j => vehicleIds.Contains(j.VehicleId)).ToListAsync()
                : new List<MaintenanceJob>();

            var filteredMaintenance = allMaintenance.Where(j => j.CreatedAt >= filterStart && j.CreatedAt < filterEnd).ToList();
            ViewBag.PendingMaintenance = allMaintenance.Count(j => j.JobStatus == MaintenanceJobStatus.Pending);
            ViewBag.InProgressMaintenance = allMaintenance.Count(j => j.JobStatus == MaintenanceJobStatus.InProgress);
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
                // Monthly within the range
                var cursor = new DateTime(filterStart.Year, filterStart.Month, 1);
                if (filter == "month")
                {
                    cursor = today.AddMonths(-5);
                    cursor = new DateTime(cursor.Year, cursor.Month, 1);
                    var chartEnd2 = new DateTime(today.Year, today.Month, 1).AddMonths(1);
                    while (cursor < chartEnd2)
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
                // Quarterly for year+ ranges
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

            // ── Recent Audit Logs (branch user activity) ─────────
            var recentLogs = await _context.AuditLogs
                .OrderByDescending(a => a.Timestamp)
                .Take(8)
                .ToListAsync();
            ViewBag.RecentAuditLogs = recentLogs;

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

            return View();
        }

        public async Task<IActionResult> Archive()
        {
            // Archived users: LockoutEnd set to MaxValue
            var allUsers = await _userManager.Users.ToListAsync();
            var archivedUsers = new List<UserViewModel>();

            var currentUser = await _userManager.GetUserAsync(User);
            var currentUserBranch = await _context.UserBranches
                .Include(ub => ub.Branch)
                .FirstOrDefaultAsync(ub => ub.UserId == currentUser!.Id);

            foreach (var u in allUsers)
            {
                if (!(u.LockoutEnabled && u.LockoutEnd.HasValue && u.LockoutEnd.Value.Year >= 9999))
                    continue;

                var roles = await _userManager.GetRolesAsync(u);

                // Apply same role-based visibility as UserManagement Index
                bool canSee = false;
                if (User.IsInRole("Super Admin"))
                {
                    canSee = true;
                }
                else if (User.IsInRole("Business Owner"))
                {
                    canSee = (roles.Contains("Staff") || roles.Contains("Mechanic") || roles.Contains("Admin")) && !roles.Contains("Super Admin");
                }
                else if (User.IsInRole("Admin"))
                {
                    if ((roles.Contains("Staff") || roles.Contains("Mechanic")) && !roles.Contains("Super Admin") && !roles.Contains("Business Owner"))
                    {
                        if (currentUserBranch?.BranchId != null)
                        {
                            var userBranch = await _context.UserBranches.FirstOrDefaultAsync(ub => ub.UserId == u.Id);
                            canSee = userBranch != null && userBranch.BranchId == currentUserBranch.BranchId;
                        }
                    }
                }

                if (!canSee) continue;

                var ub = await _context.UserBranches
                    .Include(x => x.Branch)
                    .FirstOrDefaultAsync(x => x.UserId == u.Id);

                archivedUsers.Add(new UserViewModel
                {
                    Id = u.Id,
                    Email = u.Email,
                    UserName = u.UserName,
                    Roles = roles,
                    IsBusinessOwner = roles.Contains("Business Owner"),
                    BranchName = ub?.Branch?.Name,
                    IsActive = false
                });
            }

            // Archived categories
            var archivedCategories = await _context.CategoryRates
                .Where(c => c.IsArchived)
                .OrderBy(c => c.Category)
                .ToListAsync();

            var model = new ArchiveViewModel
            {
                ArchivedUsers = archivedUsers,
                ArchivedCategories = archivedCategories
            };

            ViewBag.CanModifyCategories = User.IsInRole("Super Admin") || User.IsInRole("Business Owner");
            return View(model);
        }
    }
}

