using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DriveAway.Models
{
    public enum DamageSeverity
    {
        None,
        Minor,
        Major
    }

    public enum MaintenanceJobStatus
    {
        Pending,
        Assigned,
        InProgress,
        Completed
    }

    public enum MaintenanceType
    {
        MinorService,
        MajorService,
        GeneralInspection
    }

    public class MaintenanceJob
    {
        public int Id { get; set; }

        [Required]
        public int VehicleId { get; set; }

        [ForeignKey("VehicleId")]
        public Vehicle Vehicle { get; set; } = null!;

        public int? RentalContractId { get; set; }

        [ForeignKey("RentalContractId")]
        public RentalContract? RentalContract { get; set; }

        [Display(Name = "Maintenance Type")]
        public MaintenanceType? MaintenanceType { get; set; }

        [Display(Name = "Scheduled at Mileage")]
        public int? ScheduledAtMileage { get; set; }

        [Required]
        [Display(Name = "Damage Severity")]
        public DamageSeverity DamageSeverity { get; set; }

        [StringLength(1000)]
        [Display(Name = "Damage Description")]
        public string? DamageDescription { get; set; }

        [Required]
        [Display(Name = "Job Status")]
        public MaintenanceJobStatus JobStatus { get; set; } = MaintenanceJobStatus.Pending;

        [StringLength(450)]
        [Display(Name = "Assigned Mechanic")]
        public string? AssignedMechanicId { get; set; }

        [StringLength(256)]
        [Display(Name = "Mechanic Email")]
        public string? AssignedMechanicEmail { get; set; }

        [Display(Name = "Assigned At")]
        public DateTime? AssignedAt { get; set; }

        [StringLength(2000)]
        [Display(Name = "Repair Notes")]
        public string? RepairNotes { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Total Repair Cost")]
        public decimal? RepairCost { get; set; }

        [Display(Name = "Completed At")]
        public DateTime? CompletedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [StringLength(256)]
        [Display(Name = "Reported By")]
        public string? CreatedByEmail { get; set; }

        // Navigation
        public ICollection<RepairPart> RepairParts { get; set; } = new List<RepairPart>();
    }
}
