using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DriveAway.Models
{
    public enum VehicleStatus
    {
        Available,
        Rented,
        Reserved,
        UnderMaintenance,
        OutOfService,
        Retired
    }

    public class Vehicle
    {
        public int Id { get; set; }

        [Required]
        [StringLength(17, MinimumLength = 17, ErrorMessage = "VIN must be exactly 17 characters.")]
        [Display(Name = "VIN")]
        public string VIN { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Display(Name = "Plate Number")]
        public string PlateNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Make { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Model { get; set; } = string.Empty;

        [Required]
        [Range(1900, 2100, ErrorMessage = "Enter a valid year.")]
        public int Year { get; set; }

        [StringLength(50)]
        public string? Category { get; set; }

        [StringLength(100)]
        [Display(Name = "Body Class")]
        public string? BodyClass { get; set; }

        [StringLength(100)]
        public string? Manufacturer { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Purchase Cost")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Enter a valid cost.")]
        public decimal PurchaseCost { get; set; }

        [Required]
        [Display(Name = "Acquisition Date")]
        public DateTime AcquisitionDate { get; set; }

        [StringLength(100)]
        public string? Supplier { get; set; }

        [Required]
        [Range(1, 50, ErrorMessage = "Enter a valid useful life (1–50 years).")]
        [Display(Name = "Useful Life (Years)")]
        public int UsefulLifeYears { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Salvage Value")]
        [Range(0, double.MaxValue, ErrorMessage = "Enter a valid salvage value.")]
        public decimal SalvageValue { get; set; }

        [Display(Name = "Initial Mileage (km)")]
        [Range(0, int.MaxValue)]
        public int InitialMileage { get; set; }

        [Display(Name = "Current Mileage (km)")]
        [Range(0, int.MaxValue)]
        public int CurrentMileage { get; set; }

        [Display(Name = "Insurance Expiry")]
        public DateTime? InsuranceExpiry { get; set; }

        [Display(Name = "Registration Expiry")]
        public DateTime? RegistrationExpiry { get; set; }

        public VehicleStatus Status { get; set; } = VehicleStatus.Available;

        [StringLength(260)]
        [Display(Name = "Vehicle Photo")]
        public string? ImagePath { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ─── Branch Assignment ───────────────────────────────────────────
        [Display(Name = "Branch")]
        public int? BranchId { get; set; }

        [ForeignKey("BranchId")]
        public Branch? Branch { get; set; }

        // ─── Financial / Depreciation Fields ─────────────────────────────
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Accumulated Depreciation")]
        public decimal AccumulatedDepreciation { get; set; }

        [Display(Name = "Disposal Date")]
        public DateTime? DisposalDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Disposal Value")]
        public decimal? DisposalValue { get; set; }

        /// <summary>Computed: PurchaseCost − AccumulatedDepreciation</summary>
        [NotMapped]
        [Display(Name = "Current Book Value")]
        public decimal CurrentBookValue => PurchaseCost - AccumulatedDepreciation;

        // ─── Maintenance Tracking ────────────────────────────────────────
        [Display(Name = "Last Maintenance Date")]
        public DateTime? LastMaintenanceDate { get; set; }

        [Display(Name = "Last Maintenance Mileage")]
        public int? LastMaintenanceMileage { get; set; }

        // ─── Navigation ──────────────────────────────────────────────────
        public ICollection<VehicleLifecycleEvent> LifecycleEvents { get; set; } = new List<VehicleLifecycleEvent>();

        public ICollection<RentalContract> RentalContracts { get; set; } = new List<RentalContract>();

        public ICollection<DisposalRequest> DisposalRequests { get; set; } = new List<DisposalRequest>();
    }
}
