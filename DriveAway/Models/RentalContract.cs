using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DriveAway.Models
{
    public enum PaymentStatus
    {
        Pending,
        Paid
    }

    public enum RentalStatus
    {
        Active,
        Completed,
        Cancelled
    }

    public enum DepositStatus
    {
        Held,
        [Display(Name = "Fully Refunded")]
        FullyRefunded,
        [Display(Name = "Partially Refunded")]
        PartiallyRefunded,
        Forfeited
    }

    public class RentalContract
    {
        public int Id { get; set; }

        [Required]
        public int VehicleId { get; set; }

        public Vehicle Vehicle { get; set; } = null!;

        [Required]
        [StringLength(30)]
        [Display(Name = "Contract Number")]
        public string ContractNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Display(Name = "Customer Name")]
        [RegularExpression(@"^[\p{L}\p{M}\s.'\-]+$", ErrorMessage = "Customer Name contains invalid characters.")]
        public string CustomerName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Display(Name = "Customer Contact")]
        [RegularExpression(@"^[\d\s\+\-\(\)]+$", ErrorMessage = "Customer Contact contains invalid characters.")]
        public string CustomerContact { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        [Display(Name = "Customer License")]
        public string CustomerLicense { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Rental Start")]
        public DateTime RentalStart { get; set; }

        [Required]
        [Display(Name = "Rental End")]
        public DateTime RentalEnd { get; set; }

        [Display(Name = "Actual Return")]
        public DateTime? ActualReturn { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Daily Rate")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Enter a valid daily rate.")]
        public decimal DailyRate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Total Fee")]
        public decimal TotalFee { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Final Fee")]
        public decimal? FinalFee { get; set; }

        [Display(Name = "Rental Status")]
        public RentalStatus RentalStatus { get; set; } = RentalStatus.Active;

        [StringLength(50)]
        [Display(Name = "Return Fuel Level")]
        public string? ReturnFuelLevel { get; set; }

        [StringLength(500)]
        [Display(Name = "Damage Notes")]
        public string? ReturnDamageNotes { get; set; }

        [Display(Name = "Return Mileage (km)")]
        [Range(0, int.MaxValue)]
        public int? ReturnMileage { get; set; }

        [Required]
        [Display(Name = "Processed By")]
        public string ProcessedByUserId { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "Processed By (Email)")]
        public string? ProcessedByEmail { get; set; }

        [StringLength(100)]
        [EmailAddress]
        [Display(Name = "Customer Email")]
        public string? CustomerEmail { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Security Deposit")]
        [Range(5000, double.MaxValue, ErrorMessage = "Security deposit must be at least ₱5,000.")]
        public decimal SecurityDeposit { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Late Fee")]
        public decimal LateFee { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Damage Fee")]
        public decimal DamageFee { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Fuel Fee")]
        public decimal FuelFee { get; set; }

        [Display(Name = "Deposit Status")]
        public DepositStatus DepositStatus { get; set; } = DepositStatus.Held;

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Deposit Refund Amount")]
        public decimal DepositRefundAmount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
