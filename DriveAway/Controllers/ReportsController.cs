using DriveAway.Data;
using DriveAway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DriveAway.Services;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Admin,Super Admin,Business Owner")]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IReportExportService _exportService;

        public ReportsController(ApplicationDbContext context, IReportExportService exportService)
        {
            _context = context;
            _exportService = exportService;
        }

        // ───────── Helper: resolve date range from filter params ─────────
        private (DateTime? from, DateTime? to, string label) ResolveDateRange(
            string? range, DateTime? from, DateTime? to)
        {
            var now = DateTime.Now;

            return range switch
            {
                "today" => (now.Date, now.Date.AddDays(1).AddTicks(-1), "Today"),
                "month" => (new DateTime(now.Year, now.Month, 1),
                            new DateTime(now.Year, now.Month, 1).AddMonths(1).AddTicks(-1),
                            now.ToString("MMMM yyyy")),
                "custom" when from.HasValue && to.HasValue
                    => (from.Value.Date, to.Value.Date.AddDays(1).AddTicks(-1),
                        $"{from.Value:MMM d, yyyy} – {to.Value:MMM d, yyyy}"),
                _ => (null, null, "All Time")
            };
        }

        private void SetDateFilterViewData(string? range, DateTime? from, DateTime? to, string label)
        {
            ViewBag.DateRange = range ?? "";
            ViewBag.DateFrom = from?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.DateTo = to?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.DateLabel = label;
        }

        // ───────── Asset Inventory Report ─────────
        public async Task<IActionResult> AssetInventory(string? range, DateTime? from, DateTime? to, string? export)
        {
            var (start, end, label) = ResolveDateRange(range, from, to);
            SetDateFilterViewData(range, from, to, label);

            var query = _context.Vehicles.Include(v => v.Branch).AsQueryable();

            if (start.HasValue && end.HasValue)
                query = query.Where(v => v.CreatedAt >= start.Value && v.CreatedAt <= end.Value);

            var vehicles = await query.OrderBy(v => v.PlateNumber).ToListAsync();

            if (export == "excel" || export == "pdf")
            {
                var dt = new System.Data.DataTable();
                dt.Columns.Add("VIN");
                dt.Columns.Add("Plate Number");
                dt.Columns.Add("Make / Model");
                dt.Columns.Add("Year");
                dt.Columns.Add("Category");
                dt.Columns.Add("Branch");
                dt.Columns.Add("Status");

                foreach (var v in vehicles)
                {
                    dt.Rows.Add(v.VIN, v.PlateNumber, $"{v.Make} {v.Model}", v.Year.ToString(), v.Category, v.Branch?.Name, v.Status.ToString());
                }

                if (export == "excel")
                    return File(_exportService.ExportToExcel(dt, "Asset Inventory"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "AssetInventory.xlsx");
                
                if (export == "pdf")
                    return File(_exportService.ExportToPdfFromDataTable(dt, "Asset Inventory"), "application/pdf", "AssetInventory.pdf");
            }

            return View(vehicles);
        }

        // ───────── Rental Transaction Report ─────────
        public async Task<IActionResult> RentalTransactions(string? range, DateTime? from, DateTime? to, string? export)
        {
            var (start, end, label) = ResolveDateRange(range, from, to);
            SetDateFilterViewData(range, from, to, label);

            var query = _context.RentalContracts
                .Include(c => c.Vehicle)
                .Include(c => c.Payments)
                .AsQueryable();

            if (start.HasValue && end.HasValue)
                query = query.Where(c => c.CreatedAt >= start.Value && c.CreatedAt <= end.Value);

            var contracts = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();

            if (export == "excel" || export == "pdf")
            {
                var dt = new System.Data.DataTable();
                dt.Columns.Add("Contract No.");
                dt.Columns.Add("Date Created");
                dt.Columns.Add("Customer Name");
                dt.Columns.Add("Vehicle");
                dt.Columns.Add("Period");
                dt.Columns.Add("Total Fee");
                dt.Columns.Add("Status");

                foreach (var c in contracts)
                {
                    dt.Rows.Add(
                        c.ContractNumber, 
                        c.CreatedAt.ToString("MMM dd, yyyy"), 
                        c.CustomerName, 
                        c.Vehicle != null ? $"{c.Vehicle.PlateNumber} - {c.Vehicle.Make} {c.Vehicle.Model}" : "—",
                        $"{c.RentalStart:MMM dd} - {c.RentalEnd:MMM dd, yyyy}", 
                        c.FinalFee?.ToString("N2") ?? c.TotalFee.ToString("N2"), 
                        c.RentalStatus.ToString()
                    );
                }

                if (export == "excel")
                    return File(_exportService.ExportToExcel(dt, "Rental Transactions"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "RentalTransactions.xlsx");
                if (export == "pdf")
                    return File(_exportService.ExportToPdfFromDataTable(dt, "Rental Transactions"), "application/pdf", "RentalTransactions.pdf");
            }

            return View(contracts);
        }

        // ───────── Vehicle Status Report ─────────
        public async Task<IActionResult> VehicleStatus(string? export)
        {
            var vehicles = await _context.Vehicles
                .Include(v => v.Branch)
                .OrderBy(v => v.Status)
                .ThenBy(v => v.PlateNumber)
                .ToListAsync();

            // No date filter for status snapshot
            ViewBag.DateRange = "";
            ViewBag.DateLabel = "Current Snapshot";

            if (export == "excel" || export == "pdf")
            {
                var dt = new System.Data.DataTable();
                dt.Columns.Add("Plate Number");
                dt.Columns.Add("Make / Model");
                dt.Columns.Add("Year");
                dt.Columns.Add("Category");
                dt.Columns.Add("Branch");
                dt.Columns.Add("Mileage (km)");
                dt.Columns.Add("Status");

                foreach (var v in vehicles)
                {
                    dt.Rows.Add(v.PlateNumber, $"{v.Make} {v.Model}", v.Year.ToString(), v.Category, v.Branch?.Name, v.CurrentMileage.ToString("N0"), v.Status.ToString());
                }

                if (export == "excel")
                    return File(_exportService.ExportToExcel(dt, "Vehicle Status"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "VehicleStatus.xlsx");
                if (export == "pdf")
                    return File(_exportService.ExportToPdfFromDataTable(dt, "Vehicle Status"), "application/pdf", "VehicleStatus.pdf");
            }

            return View(vehicles);
        }

        // ───────── Maintenance Records Report ─────────
        public async Task<IActionResult> MaintenanceRecords(string? range, DateTime? from, DateTime? to, string? export)
        {
            var (start, end, label) = ResolveDateRange(range, from, to);
            SetDateFilterViewData(range, from, to, label);

            var query = _context.MaintenanceJobs
                .Include(j => j.Vehicle)
                .Include(j => j.RentalContract)
                .Include(j => j.RepairParts)
                .AsQueryable();

            if (start.HasValue && end.HasValue)
                query = query.Where(j => j.CreatedAt >= start.Value && j.CreatedAt <= end.Value);

            var jobs = await query.OrderByDescending(j => j.CreatedAt).ToListAsync();

            if (export == "excel" || export == "pdf")
            {
                var dt = new System.Data.DataTable();
                dt.Columns.Add("Vehicle");
                dt.Columns.Add("Severity");
                dt.Columns.Add("Description");
                dt.Columns.Add("Mechanic");
                dt.Columns.Add("Service Date");
                dt.Columns.Add("Completed");
                dt.Columns.Add("Repair Cost");
                dt.Columns.Add("Parts Used");
                dt.Columns.Add("Status");

                foreach (var j in jobs)
                {
                    dt.Rows.Add(
                        j.Vehicle != null ? $"{j.Vehicle.PlateNumber} - {j.Vehicle.Make} {j.Vehicle.Model}" : "—",
                        j.DamageSeverity.ToString(),
                        j.DamageDescription,
                        j.AssignedMechanicEmail ?? "Unassigned",
                        j.CreatedAt.ToString("MMM d, yyyy"),
                        j.CompletedAt?.ToString("MMM d, yyyy") ?? "—",
                        j.RepairCost?.ToString("N2") ?? "—",
                        j.RepairParts?.Count > 0 ? $"{j.RepairParts.Count} part(s)" : "—",
                        j.JobStatus.ToString()
                    );
                }

                if (export == "excel")
                    return File(_exportService.ExportToExcel(dt, "Maintenance Records"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "MaintenanceRecords.xlsx");
                if (export == "pdf")
                    return File(_exportService.ExportToPdfFromDataTable(dt, "Maintenance Records"), "application/pdf", "MaintenanceRecords.pdf");
            }

            return View(jobs);
        }

        // ───────── Payment Transactions Report ─────────
        public async Task<IActionResult> PaymentTransactions(string? range, DateTime? from, DateTime? to, string? export)
        {
            var (start, end, label) = ResolveDateRange(range, from, to);
            SetDateFilterViewData(range, from, to, label);

            var query = _context.Payments
                .Include(p => p.RentalContract)
                    .ThenInclude(c => c.Vehicle)
                .AsQueryable();

            if (start.HasValue && end.HasValue)
                query = query.Where(p => p.CreatedAt >= start.Value && p.CreatedAt <= end.Value);

            var payments = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();

            if (export == "excel" || export == "pdf")
            {
                var dt = new System.Data.DataTable();
                dt.Columns.Add("Date");
                dt.Columns.Add("Contract");
                dt.Columns.Add("Vehicle");
                dt.Columns.Add("Customer");
                dt.Columns.Add("Type");
                dt.Columns.Add("Method");
                dt.Columns.Add("Amount");
                dt.Columns.Add("Status");
                dt.Columns.Add("Notes");

                foreach (var p in payments)
                {
                    dt.Rows.Add(
                        p.CreatedAt.ToString("MMM d, yyyy"),
                        p.RentalContract?.ContractNumber ?? "—",
                        p.RentalContract?.Vehicle != null ? $"{p.RentalContract.Vehicle.PlateNumber} - {p.RentalContract.Vehicle.Make} {p.RentalContract.Vehicle.Model}" : "—",
                        p.RentalContract?.CustomerName ?? "—",
                        p.PaymentType.ToString(),
                        p.PaymentMethod.ToString(),
                        p.Amount.ToString("N2"),
                        p.PaymentStatus.ToString(),
                        p.Notes ?? "—"
                    );
                }

                if (export == "excel")
                    return File(_exportService.ExportToExcel(dt, "Payment Transactions"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PaymentTransactions.xlsx");
                if (export == "pdf")
                    return File(_exportService.ExportToPdfFromDataTable(dt, "Payment Transactions"), "application/pdf", "PaymentTransactions.pdf");
            }

            return View(payments);
        }

        // ───────── Damage and Repair Report ─────────
        public async Task<IActionResult> DamageRepair(string? range, DateTime? from, DateTime? to, string? export)
        {
            var (start, end, label) = ResolveDateRange(range, from, to);
            SetDateFilterViewData(range, from, to, label);

            var query = _context.MaintenanceJobs
                .Include(j => j.Vehicle)
                .Include(j => j.RentalContract)
                .Include(j => j.RepairParts)
                .Where(j => j.DamageSeverity != DamageSeverity.None)
                .AsQueryable();

            if (start.HasValue && end.HasValue)
                query = query.Where(j => j.CreatedAt >= start.Value && j.CreatedAt <= end.Value);

            var jobs = await query.OrderByDescending(j => j.CreatedAt).ToListAsync();

            if (export == "excel" || export == "pdf")
            {
                var dt = new System.Data.DataTable();
                dt.Columns.Add("Vehicle");
                dt.Columns.Add("Contract");
                dt.Columns.Add("Customer");
                dt.Columns.Add("Severity");
                dt.Columns.Add("Damage Description");
                dt.Columns.Add("Reported");
                dt.Columns.Add("Repair Cost");
                dt.Columns.Add("Parts");
                dt.Columns.Add("Repair Status");

                foreach (var j in jobs)
                {
                    var partsSummary = j.RepairParts?.Any() == true
                        ? string.Join(", ", j.RepairParts.Select(rp => $"{rp.PartName} (₱{rp.TotalCost:N2})"))
                        : "—";

                    dt.Rows.Add(
                        j.Vehicle != null ? $"{j.Vehicle.PlateNumber} - {j.Vehicle.Make} {j.Vehicle.Model}" : "—",
                        j.RentalContract?.ContractNumber ?? "—",
                        j.RentalContract?.CustomerName ?? "—",
                        j.DamageSeverity.ToString(),
                        j.DamageDescription ?? "—",
                        j.CreatedAt.ToString("MMM d, yyyy"),
                        j.RepairCost?.ToString("N2") ?? "—",
                        partsSummary,
                        j.JobStatus.ToString()
                    );
                }

                if (export == "excel")
                    return File(_exportService.ExportToExcel(dt, "Damage and Repair"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "DamageAndRepair.xlsx");
                if (export == "pdf")
                    return File(_exportService.ExportToPdfFromDataTable(dt, "Damage and Repair"), "application/pdf", "DamageAndRepair.pdf");
            }

            return View(jobs);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  ANALYTICAL REPORTS
        // ═══════════════════════════════════════════════════════════════════

        // ───────── Asset Depreciation Report ─────────
        public async Task<IActionResult> AssetDepreciation(string? export)
        {
            var vehicles = await _context.Vehicles
                .Include(v => v.Branch)
                .OrderBy(v => v.PlateNumber)
                .ToListAsync();

            // Calculate depreciation data for each vehicle
            var now = DateTime.Now;
            var depreciationData = vehicles.Select(v =>
            {
                var ageYears = Math.Max(0, (now - v.AcquisitionDate).TotalDays / 365.25);
                var depreciableAmount = v.PurchaseCost - v.SalvageValue;
                var annualDepreciation = v.UsefulLifeYears > 0
                    ? depreciableAmount / v.UsefulLifeYears
                    : 0;
                var totalDepreciation = Math.Min(depreciableAmount, annualDepreciation * (decimal)ageYears);
                var bookValue = v.PurchaseCost - totalDepreciation;
                var remainingLife = Math.Max(0, v.UsefulLifeYears - ageYears);
                var depreciationPct = v.PurchaseCost > 0
                    ? (totalDepreciation / v.PurchaseCost) * 100
                    : 0;

                return new
                {
                    Vehicle = v,
                    AgeYears = Math.Round(ageYears, 1),
                    AnnualDepreciation = Math.Round(annualDepreciation, 2),
                    TotalDepreciation = Math.Round(totalDepreciation, 2),
                    BookValue = Math.Round(bookValue, 2),
                    RemainingLife = Math.Round(remainingLife, 1),
                    DepreciationPct = Math.Round(depreciationPct, 1)
                };
            }).ToList();

            ViewBag.DepreciationData = depreciationData;
            ViewBag.TotalPurchaseCost = vehicles.Sum(v => v.PurchaseCost);
            ViewBag.TotalBookValue = depreciationData.Sum(d => d.BookValue);
            ViewBag.TotalDepreciation = depreciationData.Sum(d => d.TotalDepreciation);
            ViewBag.AvgAge = depreciationData.Any() ? depreciationData.Average(d => d.AgeYears) : 0;

            if (export == "excel" || export == "pdf")
            {
                var dt = new System.Data.DataTable();
                dt.Columns.Add("Vehicle");
                dt.Columns.Add("Year");
                dt.Columns.Add("Acquisition Date");
                dt.Columns.Add("Cost (₱)");
                dt.Columns.Add("Salvage (₱)");
                dt.Columns.Add("Age (Yrs)");
                dt.Columns.Add("Useful Life Remaining");
                dt.Columns.Add("Total Dep. (₱)");
                dt.Columns.Add("Book Value (₱)");

                foreach (var d in depreciationData)
                {
                    dt.Rows.Add(
                        $"{d.Vehicle.PlateNumber} - {d.Vehicle.Make} {d.Vehicle.Model}",
                        d.Vehicle.Year.ToString(),
                        d.Vehicle.AcquisitionDate.ToString("MMM d, yyyy"),
                        d.Vehicle.PurchaseCost.ToString("N2"),
                        d.Vehicle.SalvageValue.ToString("N2"),
                        d.AgeYears.ToString("N1"),
                        d.RemainingLife.ToString("N1"),
                        d.TotalDepreciation.ToString("N2"),
                        d.BookValue.ToString("N2")
                    );
                }

                if (export == "excel")
                    return File(_exportService.ExportToExcel(dt, "Asset Depreciation"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "AssetDepreciation.xlsx");
                if (export == "pdf")
                    return File(_exportService.ExportToPdfFromDataTable(dt, "Asset Depreciation"), "application/pdf", "AssetDepreciation.pdf");
            }

            return View(vehicles);
        }

        // ───────── Vehicle Utilization Report ─────────
        public async Task<IActionResult> VehicleUtilization(string? range, DateTime? from, DateTime? to, string? export)
        {
            var (start, end, label) = ResolveDateRange(range, from, to);
            SetDateFilterViewData(range, from, to, label);

            var vehicles = await _context.Vehicles
                .Include(v => v.Branch)
                .Include(v => v.RentalContracts)
                .OrderBy(v => v.PlateNumber)
                .ToListAsync();

            // Calculate utilization for each vehicle
            var now = DateTime.Now;
            var utilizationData = vehicles.Select(v =>
            {
                var contracts = v.RentalContracts?.AsEnumerable() ?? Enumerable.Empty<RentalContract>();

                // Apply date filter to contracts
                if (start.HasValue && end.HasValue)
                    contracts = contracts.Where(c => c.RentalStart <= end.Value && c.RentalEnd >= start.Value);

                var contractList = contracts.ToList();
                var totalRentals = contractList.Count;
                var completedRentals = contractList.Count(c => c.RentalStatus == RentalStatus.Completed);
                var activeRentals = contractList.Count(c => c.RentalStatus == RentalStatus.Active);

                // Total rental days
                var totalRentalDays = contractList.Sum(c =>
                {
                    var cStart = c.RentalStart;
                    var cEnd = c.ActualReturn ?? c.RentalEnd;
                    if (start.HasValue && cStart < start.Value) cStart = start.Value;
                    if (end.HasValue && cEnd > end.Value) cEnd = end.Value;
                    return Math.Max(0, (cEnd - cStart).TotalDays);
                });

                // Calculate available days (from acquisition or filter start to now or filter end)
                var periodStart = start ?? v.AcquisitionDate;
                var periodEnd = end ?? now;
                if (v.AcquisitionDate > periodStart) periodStart = v.AcquisitionDate;
                var availableDays = Math.Max(1, (periodEnd - periodStart).TotalDays);

                var utilizationRate = availableDays > 0 ? (totalRentalDays / availableDays) * 100 : 0;

                // Revenue
                var totalRevenue = contractList.Sum(c => c.FinalFee ?? c.TotalFee);

                return new
                {
                    Vehicle = v,
                    TotalRentals = totalRentals,
                    CompletedRentals = completedRentals,
                    ActiveRentals = activeRentals,
                    TotalRentalDays = Math.Round(totalRentalDays, 0),
                    AvailableDays = Math.Round(availableDays, 0),
                    UtilizationRate = Math.Round(Math.Min(100, utilizationRate), 1),
                    TotalRevenue = totalRevenue
                };
            }).ToList();

            ViewBag.UtilizationData = utilizationData;
            ViewBag.AvgUtilization = utilizationData.Any() ? Math.Round(utilizationData.Average(u => u.UtilizationRate), 1) : 0;
            ViewBag.TotalRevenue = utilizationData.Sum(u => u.TotalRevenue);
            ViewBag.TotalRentals = utilizationData.Sum(u => u.TotalRentals);
            ViewBag.MostUtilized = utilizationData.OrderByDescending(u => u.UtilizationRate).FirstOrDefault()?.Vehicle?.PlateNumber ?? "—";

            if (export == "excel" || export == "pdf")
            {
                var dt = new System.Data.DataTable();
                dt.Columns.Add("Vehicle");
                dt.Columns.Add("Branch");
                dt.Columns.Add("Category");
                dt.Columns.Add("Total Rentals");
                dt.Columns.Add("Active");
                dt.Columns.Add("Completed");
                dt.Columns.Add("Rental Days");
                dt.Columns.Add("Available Days");
                dt.Columns.Add("Utilization (%)");
                dt.Columns.Add("Revenue (₱)");

                foreach (var u in utilizationData.OrderByDescending((Func<dynamic, object>)(x => (double)x.UtilizationRate)))
                {
                    var v = (Vehicle)u.Vehicle;
                    dt.Rows.Add(
                        $"{v.PlateNumber} - {v.Make} {v.Model}",
                        v.Branch?.Name ?? "—",
                        v.Category ?? "—",
                        u.TotalRentals.ToString(),
                        u.ActiveRentals.ToString(),
                        u.CompletedRentals.ToString(),
                        u.TotalRentalDays.ToString(),
                        u.AvailableDays.ToString(),
                        ((double)u.UtilizationRate).ToString("N1"),
                        ((decimal)u.TotalRevenue).ToString("N2")
                    );
                }

                if (export == "excel")
                    return File(_exportService.ExportToExcel(dt, "Vehicle Utilization"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "VehicleUtilization.xlsx");
                if (export == "pdf")
                    return File(_exportService.ExportToPdfFromDataTable(dt, "Vehicle Utilization"), "application/pdf", "VehicleUtilization.pdf");
            }

            return View(vehicles);
        }

        // ───────── Maintenance Cost Analysis Report ─────────
        public async Task<IActionResult> MaintenanceCostAnalysis(string? range, DateTime? from, DateTime? to, string? export)
        {
            var (start, end, label) = ResolveDateRange(range, from, to);
            SetDateFilterViewData(range, from, to, label);

            var vehicles = await _context.Vehicles
                .Include(v => v.Branch)
                .OrderBy(v => v.PlateNumber)
                .ToListAsync();

            // Get all maintenance jobs with parts
            var jobsQuery = _context.MaintenanceJobs
                .Include(j => j.RepairParts)
                .AsQueryable();

            if (start.HasValue && end.HasValue)
                jobsQuery = jobsQuery.Where(j => j.CreatedAt >= start.Value && j.CreatedAt <= end.Value);

            var allJobs = await jobsQuery.ToListAsync();

            var costData = vehicles.Select(v =>
            {
                var vehicleJobs = allJobs.Where(j => j.VehicleId == v.Id).ToList();
                var totalJobs = vehicleJobs.Count;
                var completedJobs = vehicleJobs.Count(j => j.JobStatus == MaintenanceJobStatus.Completed);
                var repairCost = vehicleJobs.Where(j => j.RepairCost.HasValue).Sum(j => j.RepairCost!.Value);
                var partsCost = vehicleJobs.SelectMany(j => j.RepairParts).Sum(rp => rp.TotalCost);
                var totalCost = repairCost + partsCost;
                var costPercentOfValue = v.PurchaseCost > 0 ? (totalCost / v.PurchaseCost) * 100 : 0;

                return new
                {
                    Vehicle = v,
                    TotalJobs = totalJobs,
                    CompletedJobs = completedJobs,
                    RepairCost = repairCost,
                    PartsCost = partsCost,
                    TotalCost = totalCost,
                    CostPercentOfValue = Math.Round(costPercentOfValue, 1)
                };
            }).OrderByDescending(c => c.TotalCost).ToList();

            ViewBag.CostData = costData;
            ViewBag.FleetTotalCost = costData.Sum(c => c.TotalCost);
            ViewBag.FleetRepairCost = costData.Sum(c => c.RepairCost);
            ViewBag.FleetPartsCost = costData.Sum(c => c.PartsCost);
            ViewBag.HighCostCount = costData.Count(c => c.CostPercentOfValue > 10);

            if (export == "excel" || export == "pdf")
            {
                var dt = new System.Data.DataTable();
                dt.Columns.Add("Vehicle");
                dt.Columns.Add("Branch");
                dt.Columns.Add("Purchase Cost (₱)");
                dt.Columns.Add("Total Jobs");
                dt.Columns.Add("Completed");
                dt.Columns.Add("Repair Cost (₱)");
                dt.Columns.Add("Parts Cost (₱)");
                dt.Columns.Add("Total Cost (₱)");
                dt.Columns.Add("Cost % of Value");

                foreach (var c in costData)
                {
                    var v = (Vehicle)c.Vehicle;
                    dt.Rows.Add(
                        $"{v.PlateNumber} - {v.Make} {v.Model}",
                        v.Branch?.Name ?? "—",
                        v.PurchaseCost.ToString("N2"),
                        c.TotalJobs.ToString(),
                        c.CompletedJobs.ToString(),
                        ((decimal)c.RepairCost).ToString("N2"),
                        ((decimal)c.PartsCost).ToString("N2"),
                        ((decimal)c.TotalCost).ToString("N2"),
                        ((decimal)c.CostPercentOfValue).ToString("N1")
                    );
                }

                if (export == "excel")
                    return File(_exportService.ExportToExcel(dt, "Maintenance Cost Analysis"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "MaintenanceCostAnalysis.xlsx");
                if (export == "pdf")
                    return File(_exportService.ExportToPdfFromDataTable(dt, "Maintenance Cost Analysis"), "application/pdf", "MaintenanceCostAnalysis.pdf");
            }

            return View(vehicles);
        }

        // ───────── Revenue Analysis Report ─────────
        public async Task<IActionResult> RevenueAnalysis(string? range, DateTime? from, DateTime? to, string? export)
        {
            var (start, end, label) = ResolveDateRange(range, from, to);
            SetDateFilterViewData(range, from, to, label);

            var paymentsQuery = _context.Payments
                .Where(p => p.PaymentStatus == PaymentStatus.Paid
                    && p.PaymentType != PaymentType.DepositRefund
                    && p.PaymentType != PaymentType.SecurityDeposit)
                .AsQueryable();

            if (start.HasValue && end.HasValue)
                paymentsQuery = paymentsQuery.Where(p => p.CreatedAt >= start.Value && p.CreatedAt <= end.Value);

            var payments = await paymentsQuery.OrderBy(p => p.CreatedAt).ToListAsync();

            // Daily breakdown
            var dailyRevenue = payments
                .GroupBy(p => p.CreatedAt.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(p => p.Amount), Count = g.Count() })
                .OrderBy(g => g.Date)
                .ToList();

            // Monthly breakdown
            var monthlyRevenue = payments
                .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month })
                .Select(g => new { Year = g.Key.Year, Month = g.Key.Month, Total = g.Sum(p => p.Amount), Count = g.Count() })
                .OrderBy(g => g.Year).ThenBy(g => g.Month)
                .ToList();

            // Revenue by type
            var revenueByType = payments
                .GroupBy(p => p.PaymentType)
                .Select(g => new { Type = g.Key, Total = g.Sum(p => p.Amount) })
                .OrderByDescending(g => g.Total)
                .ToList();

            ViewBag.DailyRevenue = dailyRevenue;
            ViewBag.MonthlyRevenue = monthlyRevenue;
            ViewBag.RevenueByType = revenueByType;
            ViewBag.TotalRevenue = payments.Sum(p => p.Amount);
            ViewBag.TransactionCount = payments.Count;
            ViewBag.AvgTransaction = payments.Any() ? payments.Average(p => p.Amount) : 0;
            ViewBag.PeakDay = dailyRevenue.OrderByDescending(d => d.Total).FirstOrDefault();

            if (export == "excel" || export == "pdf")
            {
                var dt = new System.Data.DataTable();
                dt.Columns.Add("Date");
                dt.Columns.Add("Contract");
                dt.Columns.Add("Customer");
                dt.Columns.Add("Revenue Type");
                dt.Columns.Add("Amount (₱)");

                foreach (var p in payments)
                {
                    // For revenue analysis, we want a clean list of all revenue-generating transactions
                    dt.Rows.Add(
                        p.CreatedAt.ToString("MMM d, yyyy"),
                        p.RentalContract?.ContractNumber ?? "—",
                        p.RentalContract?.CustomerName ?? "—",
                        p.PaymentType.ToString(),
                        p.Amount.ToString("N2")
                    );
                }

                if (export == "excel")
                    return File(_exportService.ExportToExcel(dt, "Revenue Analysis"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "RevenueAnalysis.xlsx");
                if (export == "pdf")
                    return File(_exportService.ExportToPdfFromDataTable(dt, "Revenue Analysis"), "application/pdf", "RevenueAnalysis.pdf");
            }

            return View();
        }

        // ───────── Profit and Loss Report ─────────
        public async Task<IActionResult> ProfitAndLoss(string? range, DateTime? from, DateTime? to, string? export)
        {
            var (start, end, label) = ResolveDateRange(range, from, to);
            SetDateFilterViewData(range, from, to, label);

            // Income: Paid rental + fee payments (exclude deposits and refunds)
            var incomeQuery = _context.Payments
                .Where(p => p.PaymentStatus == PaymentStatus.Paid
                    && p.PaymentType != PaymentType.SecurityDeposit
                    && p.PaymentType != PaymentType.DepositRefund)
                .AsQueryable();

            if (start.HasValue && end.HasValue)
                incomeQuery = incomeQuery.Where(p => p.CreatedAt >= start.Value && p.CreatedAt <= end.Value);

            var incomePayments = await incomeQuery.ToListAsync();

            var rentalIncome = incomePayments.Where(p => p.PaymentType == PaymentType.Rental).Sum(p => p.Amount);
            var lateFeeIncome = incomePayments.Where(p => p.PaymentType == PaymentType.LateFee).Sum(p => p.Amount);
            var damageFeeIncome = incomePayments.Where(p => p.PaymentType == PaymentType.DamageFee).Sum(p => p.Amount);
            var fuelFeeIncome = incomePayments.Where(p => p.PaymentType == PaymentType.FuelFee).Sum(p => p.Amount);
            var totalIncome = incomePayments.Sum(p => p.Amount);

            // Expenses: Maintenance costs
            var expenseQuery = _context.MaintenanceJobs
                .Include(j => j.RepairParts)
                .AsQueryable();

            if (start.HasValue && end.HasValue)
                expenseQuery = expenseQuery.Where(j => j.CreatedAt >= start.Value && j.CreatedAt <= end.Value);

            var jobs = await expenseQuery.ToListAsync();

            var repairLabor = jobs.Where(j => j.RepairCost.HasValue).Sum(j => j.RepairCost!.Value);
            var partsCost = jobs.SelectMany(j => j.RepairParts).Sum(rp => rp.TotalCost);
            var totalExpenses = repairLabor + partsCost;

            // Refunds as expense
            var refundQuery = _context.Payments
                .Where(p => p.PaymentStatus == PaymentStatus.Paid && p.PaymentType == PaymentType.DepositRefund)
                .AsQueryable();

            if (start.HasValue && end.HasValue)
                refundQuery = refundQuery.Where(p => p.CreatedAt >= start.Value && p.CreatedAt <= end.Value);

            var totalRefunds = await refundQuery.SumAsync(p => (decimal?)p.Amount) ?? 0;

            ViewBag.RentalIncome = rentalIncome;
            ViewBag.LateFeeIncome = lateFeeIncome;
            ViewBag.DamageFeeIncome = damageFeeIncome;
            ViewBag.FuelFeeIncome = fuelFeeIncome;
            ViewBag.TotalIncome = totalIncome;
            ViewBag.RepairLabor = repairLabor;
            ViewBag.PartsCost = partsCost;
            ViewBag.TotalRefunds = totalRefunds;
            ViewBag.TotalExpenses = totalExpenses + totalRefunds;
            ViewBag.NetProfit = totalIncome - (totalExpenses + totalRefunds);

            // Monthly P&L
            var monthlyIncome = incomePayments
                .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month })
                .Select(g => new { Year = g.Key.Year, Month = g.Key.Month, Income = g.Sum(p => p.Amount) })
                .ToList();

            var monthlyExpense = jobs
                .GroupBy(j => new { j.CreatedAt.Year, j.CreatedAt.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Expense = g.Where(j => j.RepairCost.HasValue).Sum(j => j.RepairCost!.Value)
                              + g.SelectMany(j => j.RepairParts).Sum(rp => rp.TotalCost)
                })
                .ToList();

            var allMonths = monthlyIncome.Select(m => (m.Year, m.Month))
                .Union(monthlyExpense.Select(m => (m.Year, m.Month)))
                .Distinct().OrderBy(m => m.Year).ThenBy(m => m.Month).ToList();

            var monthlyPnL = allMonths.Select(m => new
            {
                m.Year,
                m.Month,
                Income = monthlyIncome.FirstOrDefault(i => i.Year == m.Year && i.Month == m.Month)?.Income ?? 0,
                Expense = monthlyExpense.FirstOrDefault(e => e.Year == m.Year && e.Month == m.Month)?.Expense ?? 0
            }).ToList();

            ViewBag.MonthlyPnL = monthlyPnL;

            if (export == "excel" || export == "pdf")
            {
                var dt = new System.Data.DataTable();
                dt.Columns.Add("Month");
                dt.Columns.Add("Total Income (₱)");
                dt.Columns.Add("Total Expenses (₱)");
                dt.Columns.Add("Net Profit (₱)");
                dt.Columns.Add("Margin (%)");

                foreach (var m in monthlyPnL)
                {
                    var mIncome = (decimal)m.Income;
                    var mExpense = (decimal)m.Expense;
                    var mNet = mIncome - mExpense;
                    var mMargin = mIncome > 0 ? (mNet / mIncome) * 100 : 0;

                    dt.Rows.Add(
                        new DateTime(m.Year, m.Month, 1).ToString("MMM yyyy"),
                        mIncome.ToString("N2"),
                        mExpense.ToString("N2"),
                        mNet.ToString("N2"),
                        mMargin.ToString("N1")
                    );
                }

                // Append aggregate totals row at the bottom
                dt.Rows.Add("TOTAL", totalIncome.ToString("N2"), (totalExpenses + totalRefunds).ToString("N2"), (totalIncome - (totalExpenses + totalRefunds)).ToString("N2"), "—");

                if (export == "excel")
                    return File(_exportService.ExportToExcel(dt, "Profit and Loss"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ProfitAndLoss.xlsx");
                if (export == "pdf")
                    return File(_exportService.ExportToPdfFromDataTable(dt, "Profit and Loss"), "application/pdf", "ProfitAndLoss.pdf");
            }

            return View();
        }

        // ───────── Payment Method Analysis Report ─────────
        public async Task<IActionResult> PaymentMethodAnalysis(string? range, DateTime? from, DateTime? to, string? export)
        {
            var (start, end, label) = ResolveDateRange(range, from, to);
            SetDateFilterViewData(range, from, to, label);

            var paymentsQuery = _context.Payments
                .Where(p => p.PaymentStatus == PaymentStatus.Paid
                    && p.PaymentType != PaymentType.DepositRefund)
                .AsQueryable();

            if (start.HasValue && end.HasValue)
                paymentsQuery = paymentsQuery.Where(p => p.CreatedAt >= start.Value && p.CreatedAt <= end.Value);

            var payments = await paymentsQuery.ToListAsync();

            // Group by method
            var byMethod = payments
                .GroupBy(p => p.PaymentMethod)
                .Select(g => new
                {
                    Method = g.Key,
                    Count = g.Count(),
                    Total = g.Sum(p => p.Amount)
                })
                .OrderByDescending(g => g.Total)
                .ToList();

            // Group by method + type
            var byMethodType = payments
                .GroupBy(p => new { p.PaymentMethod, p.PaymentType })
                .Select(g => new
                {
                    Method = g.Key.PaymentMethod,
                    Type = g.Key.PaymentType,
                    Count = g.Count(),
                    Total = g.Sum(p => p.Amount)
                })
                .OrderBy(g => g.Method).ThenByDescending(g => g.Total)
                .ToList();

            // Monthly trend by method
            var monthlyTrend = payments
                .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month, p.PaymentMethod })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Method = g.Key.PaymentMethod,
                    Total = g.Sum(p => p.Amount)
                })
                .OrderBy(g => g.Year).ThenBy(g => g.Month)
                .ToList();

            ViewBag.ByMethod = byMethod;
            ViewBag.ByMethodType = byMethodType;
            ViewBag.MonthlyTrend = monthlyTrend;
            ViewBag.TotalAmount = payments.Sum(p => p.Amount);
            ViewBag.TotalTransactions = payments.Count;

            if (export == "excel" || export == "pdf")
            {
                var dt = new System.Data.DataTable();
                dt.Columns.Add("Payment Method");
                dt.Columns.Add("Payment Type");
                dt.Columns.Add("Transaction Count");
                dt.Columns.Add("Total Processed (₱)");
                dt.Columns.Add("Share of Method (%)");

                foreach (var m in byMethodType)
                {
                    var mMethod = (PaymentMethodType)m.Method;
                    var mType = (PaymentType)m.Type;
                    var mAmount = (decimal)m.Total;
                    var mCount = (int)m.Count;

                    var methodTotalAmount = byMethod.FirstOrDefault(b => (PaymentMethodType)b.Method == mMethod)?.Total ?? 0;
                    var share = methodTotalAmount > 0 ? (mAmount / methodTotalAmount) * 100 : 0;

                    dt.Rows.Add(
                        mMethod.ToString(),
                        mType.ToString(),
                        mCount.ToString(),
                        mAmount.ToString("N2"),
                        share.ToString("N1")
                    );
                }

                if (export == "excel")
                    return File(_exportService.ExportToExcel(dt, "Payment Method Analysis"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PaymentMethodAnalysis.xlsx");
                if (export == "pdf")
                    return File(_exportService.ExportToPdfFromDataTable(dt, "Payment Method Analysis"), "application/pdf", "PaymentMethodAnalysis.pdf");
            }

            return View();
        }
    }
}
