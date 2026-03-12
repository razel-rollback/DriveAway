using DriveAway.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DriveAway.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Vehicle> Vehicles { get; set; }
        public DbSet<VehicleLifecycleEvent> VehicleLifecycleEvents { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<RentalContract> RentalContracts { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<CategoryRate> CategoryRates { get; set; }
        public DbSet<Branch> Branches { get; set; }
        public DbSet<UserBranch> UserBranches { get; set; }
        public DbSet<DisposalRequest> DisposalRequests { get; set; }
        public DbSet<MaintenanceJob> MaintenanceJobs { get; set; }
        public DbSet<RepairPart> RepairParts { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<CategoryRate>()
                .HasIndex(c => c.Category)
                .IsUnique();

            builder.Entity<UserBranch>()
                .HasIndex(ub => ub.UserId)
                .IsUnique();
        }
    }
}
