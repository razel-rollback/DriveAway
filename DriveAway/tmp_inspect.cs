using System;
using System.Linq;
using DriveAway.Data;
using DriveAway.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DriveAway.Tmp
{
    public class DataInspector
    {
        public static void Inspect(IServiceProvider services)
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            var vehicles = context.Vehicles.Include(v => v.Branch).ToList();
            
            Console.WriteLine($"Total Vehicles: {vehicles.Count}");
            foreach (var v in vehicles)
            {
                Console.WriteLine($"- Plate: {v.PlateNumber}, Status: {v.Status}, Branch: {v.Branch?.Name ?? "None"}, Insurance: {v.InsuranceExpiry}, Reg: {v.RegistrationExpiry}");
            }
            
            var today = DateTime.Today;
            var available = vehicles.Where(v => v.Status == VehicleStatus.Available
                && (v.InsuranceExpiry == null || v.InsuranceExpiry >= today)
                && (v.RegistrationExpiry == null || v.RegistrationExpiry >= today)).ToList();
            
            Console.WriteLine($"Available for Rental (Logic): {available.Count}");
        }
    }
}
