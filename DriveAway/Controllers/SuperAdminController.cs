using DriveAway.Data;
using DriveAway.Models;
using DriveAway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Super Admin")]
    public class SuperAdminController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IBackupRestoreService _backupService;
        private readonly IAuditService _auditService;

        public SuperAdminController(
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IBackupRestoreService backupService,
            IAuditService auditService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
            _httpClientFactory = httpClientFactory;
            _backupService = backupService;
            _auditService = auditService;
        }

        public async Task<IActionResult> Dashboard(string filter = "month", DateTime? startDate = null, DateTime? endDate = null)
        {
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
                "custom" => $"{filterStart:MMM dd} – {filterEnd.AddDays(-1):MMM dd, yyyy}",
                _ => "This Month"
            };
            ViewBag.FilterLabel = filterLabel;

            // ── User & Role Metrics ──────────────────────────────
            var totalUsers = _userManager.Users.Count();
            var totalRoles = _roleManager.Roles.Count();
            var superAdmins = (await _userManager.GetUsersInRoleAsync("Super Admin")).Count;
            var admins = (await _userManager.GetUsersInRoleAsync("Admin")).Count;
            var staff = (await _userManager.GetUsersInRoleAsync("Staff")).Count;
            var businessOwners = (await _userManager.GetUsersInRoleAsync("Business Owner")).Count;
            var mechanics = (await _userManager.GetUsersInRoleAsync("Mechanic")).Count;

            ViewBag.TotalUsers = totalUsers;
            ViewBag.TotalRoles = totalRoles;
            ViewBag.SuperAdmins = superAdmins;
            ViewBag.Admins = admins;
            ViewBag.Staff = staff;
            ViewBag.BusinessOwners = businessOwners;
            ViewBag.Mechanics = mechanics;

            // ── Fleet Metrics ────────────────────────────────────
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
            var contracts = await _context.RentalContracts.ToListAsync();
            var filteredContracts = contracts.Where(c => c.CreatedAt >= filterStart && c.CreatedAt < filterEnd).ToList();
            ViewBag.TotalContracts = contracts.Count;
            ViewBag.ActiveRentals = filteredContracts.Count(c => c.RentalStatus == RentalStatus.Active);
            ViewBag.CompletedRentals = filteredContracts.Count(c => c.RentalStatus == RentalStatus.Completed);
            ViewBag.OverdueRentals = filteredContracts.Count(c => c.RentalStatus == RentalStatus.Active && c.RentalEnd.Date < today);

            // ── Revenue (filtered) ───────────────────────────────
            var periodRevenue = filteredContracts
                .Sum(c => c.FinalFee ?? c.TotalFee);
            var totalRevenue = contracts
                .Where(c => c.RentalStatus == RentalStatus.Completed || c.RentalStatus == RentalStatus.Active)
                .Sum(c => c.FinalFee ?? c.TotalFee);
            ViewBag.PeriodRevenue = periodRevenue;
            ViewBag.TotalRevenue = totalRevenue;

            // ── Revenue Chart Data (adapts to filter) ────────────
            var revenueByMonth = new List<object>();
            var rentalsByMonth = new List<object>();
            var totalDays = (filterEnd - filterStart).Days;

            if (totalDays <= 1)
            {
                // Hourly breakdown for "day"
                for (int h = 0; h < 24; h += 4)
                {
                    var hStart = filterStart.AddHours(h);
                    var hEnd = filterStart.AddHours(h + 4);
                    var rev = contracts.Where(c => c.CreatedAt >= hStart && c.CreatedAt < hEnd).Sum(c => c.FinalFee ?? c.TotalFee);
                    var cnt = contracts.Count(c => c.CreatedAt >= hStart && c.CreatedAt < hEnd);
                    revenueByMonth.Add(new { Label = hStart.ToString("h tt"), Revenue = rev });
                    rentalsByMonth.Add(new { Label = hStart.ToString("h tt"), Count = cnt });
                }
            }
            else if (totalDays <= 14)
            {
                // Daily breakdown for "week" or short custom
                for (var d = filterStart; d < filterEnd; d = d.AddDays(1))
                {
                    var dEnd = d.AddDays(1);
                    var rev = contracts.Where(c => c.CreatedAt >= d && c.CreatedAt < dEnd).Sum(c => c.FinalFee ?? c.TotalFee);
                    var cnt = contracts.Count(c => c.CreatedAt >= d && c.CreatedAt < dEnd);
                    revenueByMonth.Add(new { Label = d.ToString("MMM dd"), Revenue = rev });
                    rentalsByMonth.Add(new { Label = d.ToString("MMM dd"), Count = cnt });
                }
            }
            else
            {
                // Monthly breakdown
                var cursor = new DateTime(filterStart.Year, filterStart.Month, 1);
                var chartEnd = filterEnd;
                // For default "month", show last 6 months
                if (filter == "month")
                {
                    cursor = today.AddMonths(-5);
                    cursor = new DateTime(cursor.Year, cursor.Month, 1);
                    chartEnd = new DateTime(today.Year, today.Month, 1).AddMonths(1);
                }
                while (cursor < chartEnd)
                {
                    var mEnd = cursor.AddMonths(1);
                    var rev = contracts.Where(c => c.CreatedAt >= cursor && c.CreatedAt < mEnd).Sum(c => c.FinalFee ?? c.TotalFee);
                    var cnt = contracts.Count(c => c.CreatedAt >= cursor && c.CreatedAt < mEnd);
                    revenueByMonth.Add(new { Label = cursor.ToString("MMM yyyy"), Revenue = rev });
                    rentalsByMonth.Add(new { Label = cursor.ToString("MMM yyyy"), Count = cnt });
                    cursor = mEnd;
                }
            }
            ViewBag.RevenueByMonth = revenueByMonth;
            ViewBag.RentalsByMonth = rentalsByMonth;

            // ── Maintenance Metrics (filtered) ───────────────────
            var maintenanceJobs = await _context.MaintenanceJobs.ToListAsync();
            var filteredMaintenance = maintenanceJobs.Where(j => j.CreatedAt >= filterStart && j.CreatedAt < filterEnd).ToList();
            ViewBag.PendingMaintenance = filteredMaintenance.Count(j => j.JobStatus == MaintenanceJobStatus.Pending);
            ViewBag.InProgressMaintenance = filteredMaintenance.Count(j => j.JobStatus == MaintenanceJobStatus.InProgress);
            ViewBag.CompletedMaintenance = filteredMaintenance.Count(j => j.JobStatus == MaintenanceJobStatus.Completed);
            ViewBag.TotalMaintenanceCost = filteredMaintenance.Where(j => j.RepairCost.HasValue).Sum(j => j.RepairCost!.Value);

            // ── Recent Audit Logs ────────────────────────────────
            var recentLogs = await _context.AuditLogs
                .OrderByDescending(a => a.Timestamp)
                .Take(8)
                .ToListAsync();
            ViewBag.RecentAuditLogs = recentLogs;

            // ── Vehicle Status Distribution (for doughnut chart) ─
            ViewBag.VehicleStatusLabels = new[] { "Available", "Rented", "Under Maintenance", "Out of Service", "Retired" };
            ViewBag.VehicleStatusCounts = new[]
            {
                vehicles.Count(v => v.Status == VehicleStatus.Available),
                vehicles.Count(v => v.Status == VehicleStatus.Rented),
                vehicles.Count(v => v.Status == VehicleStatus.UnderMaintenance),
                vehicles.Count(v => v.Status == VehicleStatus.OutOfService),
                vehicles.Count(v => v.Status == VehicleStatus.Retired)
            };

            // ── Payments (filtered) ──────────────────────────────
            var payments = await _context.Payments.ToListAsync();
            var filteredPayments = payments.Where(p => p.CreatedAt >= filterStart && p.CreatedAt < filterEnd).ToList();
            ViewBag.TotalPaymentsCollected = filteredPayments.Where(p => p.PaymentStatus == PaymentStatus.Paid).Sum(p => p.Amount);
            ViewBag.PendingPayments = filteredPayments.Where(p => p.PaymentStatus == PaymentStatus.Pending).Sum(p => p.Amount);

            // ── System Health ─────────────────────────────────────
            // Database size (MB)
            var dbSizeMb = 0L;
            try
            {
                var conn = _context.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                // SECURITY: Static query with zero user-supplied values — safe from SQL injection.
                // If parameters are ever added, use cmd.Parameters.Add() instead of string concatenation.
                cmd.CommandText = "SELECT COALESCE(SUM(size) * 8 / 1024, 0) FROM sys.database_files";
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    dbSizeMb = Convert.ToInt64(result);
            }
            catch { dbSizeMb = 0; }
            ViewBag.DatabaseSizeMb = dbSizeMb;

            // Server uptime
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            var uptime = DateTime.Now - proc.StartTime;
            ViewBag.ServerUptime = uptime.Days > 0
                ? $"{uptime.Days}d {uptime.Hours}h"
                : uptime.Hours > 0
                    ? $"{uptime.Hours}h {uptime.Minutes}m"
                    : $"{uptime.Minutes}m";

            // Active sessions (unique users active in last 30 min)
            var sessionCutoff = DateTime.UtcNow.AddMinutes(-30);
            ViewBag.ActiveSessions = await _context.AuditLogs
                .Where(a => a.Timestamp >= sessionCutoff && a.UserEmail != null)
                .Select(a => a.UserEmail)
                .Distinct()
                .CountAsync();

            // API status checks (run in parallel)
            var nhtsaTask = CheckApiStatusAsync(
                "https://vpic.nhtsa.dot.gov/api/vehicles/GetVehicleTypesForMakeId/440?format=json",
                code => code == System.Net.HttpStatusCode.OK);
            var pmTask = CheckApiStatusAsync(
                "https://api.paymongo.com/v1/payment_methods",
                code => code == System.Net.HttpStatusCode.OK || code == System.Net.HttpStatusCode.Unauthorized);
            await Task.WhenAll(nhtsaTask, pmTask);
            ViewBag.NhtsaApiStatus = nhtsaTask.Result;
            ViewBag.PayMongoApiStatus = pmTask.Result;

            return View();
        }

        private async Task<string> CheckApiStatusAsync(string url, Func<System.Net.HttpStatusCode, bool> isHealthy)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(4);
                var response = await client.GetAsync(url);
                return isHealthy(response.StatusCode) ? "Online" : "Degraded";
            }
            catch
            {
                return "Offline";
            }
        }

        // ── Backup & Restore ─────────────────────────────────────

        public async Task<IActionResult> BackupRestore()
        {
            var backups = await _backupService.GetBackupsAsync();
            ViewBag.Backups = backups;
            ViewBag.DatabaseName = _backupService.GetDatabaseName();

            // Find the most recent restore operation from the audit logs
            var lastRestore = await _context.AuditLogs
                .Where(a => a.Action == "Database Restored")
                .OrderByDescending(a => a.Timestamp)
                .FirstOrDefaultAsync();
            
            ViewBag.LastRestoredBackup = lastRestore?.EntityName;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBackup()
        {
            try
            {
                var backup = await _backupService.CreateBackupAsync();

                await _auditService.LogAsync(
                    action: "Database Backup Created",
                    module: "System Administration",
                    entityType: "DatabaseBackup",
                    entityName: backup.FileName,
                    details: $"Backup file created: {backup.FileName} ({FormatFileSize(backup.SizeBytes)})");

                TempData["SuccessMessage"] = $"Backup created successfully: {backup.FileName} ({FormatFileSize(backup.SizeBytes)})";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to create backup: {ex.Message}";
            }

            return RedirectToAction(nameof(BackupRestore));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreBackup(string fileName)
        {
            try
            {
                await _backupService.RestoreAsync(fileName);

                // Log AFTER restore so it persists in the new database
                await _auditService.LogAsync(
                    action: "Database Restored",
                    module: "System Administration",
                    entityType: "DatabaseBackup",
                    entityName: fileName,
                    details: $"Database restored from backup: {fileName}");

                TempData["SuccessMessage"] = $"Database restored successfully from: {fileName}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to restore database: {ex.Message}";
            }

            return RedirectToAction(nameof(BackupRestore));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBackup(string fileName)
        {
            try
            {
                await _backupService.DeleteBackupAsync(fileName);

                await _auditService.LogAsync(
                    action: "Backup Deleted",
                    module: "System Administration",
                    entityType: "DatabaseBackup",
                    entityName: fileName,
                    details: $"Backup file deleted: {fileName}");

                TempData["SuccessMessage"] = $"Backup deleted: {fileName}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to delete backup: {ex.Message}";
            }

            return RedirectToAction(nameof(BackupRestore));
        }

        public IActionResult DownloadBackup(string fileName)
        {
            var filePath = _backupService.GetBackupFilePath(fileName);
            if (filePath == null)
                return NotFound();

            return PhysicalFile(filePath, "application/octet-stream", fileName);
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1_073_741_824)
                return $"{bytes / 1_073_741_824.0:F2} GB";
            if (bytes >= 1_048_576)
                return $"{bytes / 1_048_576.0:F2} MB";
            if (bytes >= 1_024)
                return $"{bytes / 1_024.0:F2} KB";
            return $"{bytes} bytes";
        }
    }
}
