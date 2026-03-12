using DriveAway.Data;
using DriveAway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Staff")]
    public class StaffDashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StaffDashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Dashboard()
        {
            var vehicles = await _context.Vehicles.ToListAsync();
            var contracts = await _context.RentalContracts
                .Include(c => c.Vehicle)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            var today = DateTime.UtcNow.Date;
            var monthStart = new DateTime(today.Year, today.Month, 1);

            ViewBag.AvailableVehicles = vehicles.Count(v => v.Status == VehicleStatus.Available);
            ViewBag.ActiveRentals = contracts.Count(c => c.RentalStatus == RentalStatus.Active);
            ViewBag.CompletedToday = contracts.Count(c => c.RentalStatus == RentalStatus.Completed
                && c.ActualReturn.HasValue && c.ActualReturn.Value.Date == today);
            ViewBag.Overdue = contracts.Count(c => c.RentalStatus == RentalStatus.Active
                && c.RentalEnd.Date < today);
            ViewBag.TotalRented = vehicles.Count(v => v.Status == VehicleStatus.Rented);

            // Monthly revenue
            ViewBag.MonthlyRevenue = contracts
                .Where(c => c.CreatedAt >= monthStart)
                .Sum(c => c.FinalFee ?? c.TotalFee);

            // Recent active contracts (up to 5)
            ViewBag.RecentContracts = contracts
                .Where(c => c.RentalStatus == RentalStatus.Active)
                .Take(5)
                .ToList();

            return View();
        }
    }
}
