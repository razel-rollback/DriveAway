using DriveAway.Data;
using DriveAway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Admin,Super Admin,Business Owner")]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
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
        public async Task<IActionResult> AssetInventory(string? range, DateTime? from, DateTime? to)
        {
            var (start, end, label) = ResolveDateRange(range, from, to);
            SetDateFilterViewData(range, from, to, label);

            var query = _context.Vehicles.Include(v => v.Branch).AsQueryable();

            if (start.HasValue && end.HasValue)
                query = query.Where(v => v.CreatedAt >= start.Value && v.CreatedAt <= end.Value);

            var vehicles = await query.OrderBy(v => v.PlateNumber).ToListAsync();
            return View(vehicles);
        }

        // ───────── Rental Transaction Report ─────────
        public async Task<IActionResult> RentalTransactions(string? range, DateTime? from, DateTime? to)
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
            return View(contracts);
        }

        // ───────── Vehicle Status Report ─────────
        public async Task<IActionResult> VehicleStatus()
        {
            var vehicles = await _context.Vehicles
                .Include(v => v.Branch)
                .OrderBy(v => v.Status)
                .ThenBy(v => v.PlateNumber)
                .ToListAsync();

            // No date filter for status snapshot
            ViewBag.DateRange = "";
            ViewBag.DateLabel = "Current Snapshot";

            return View(vehicles);
        }

        // ───────── Maintenance Records Report ─────────
        public async Task<IActionResult> MaintenanceRecords(string? range, DateTime? from, DateTime? to)
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
            return View(jobs);
        }

        // ───────── Payment Transactions Report ─────────
        public async Task<IActionResult> PaymentTransactions(string? range, DateTime? from, DateTime? to)
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
            return View(payments);
        }

        // ───────── Damage and Repair Report ─────────
        public async Task<IActionResult> DamageRepair(string? range, DateTime? from, DateTime? to)
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
            return View(jobs);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  ANALYTICAL REPORTS
        // ═══════════════════════════════════════════════════════════════════

        // ───────── Asset Depreciation Report ─────────
        public async Task<IActionResult> AssetDepreciation()
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

            return View(vehicles);
        }

        // ───────── Vehicle Utilization Report ─────────
        public async Task<IActionResult> VehicleUtilization(string? range, DateTime? from, DateTime? to)
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

                var utilizationRate = (totalRentalDays / availableDays) * 100;

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

            return View(vehicles);
        }

        // ───────── Maintenance Cost Analysis Report ─────────
        public async Task<IActionResult> MaintenanceCostAnalysis(string? range, DateTime? from, DateTime? to)
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

            return View(vehicles);
        }

        // ───────── Revenue Analysis Report ─────────
        public async Task<IActionResult> RevenueAnalysis(string? range, DateTime? from, DateTime? to)
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

            return View();
        }

        // ───────── Profit and Loss Report ─────────
        public async Task<IActionResult> ProfitAndLoss(string? range, DateTime? from, DateTime? to)
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

            return View();
        }

        // ───────── Payment Method Analysis Report ─────────
        public async Task<IActionResult> PaymentMethodAnalysis(string? range, DateTime? from, DateTime? to)
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

            return View();
        }
    }
}
