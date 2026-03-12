using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DriveAway.Models
{
    public enum DisposalRequestStatus
    {
        Pending,
        Approved,
        Rejected
    }

    public class DisposalRequest
    {
        public int Id { get; set; }

        [Required]
        public int VehicleId { get; set; }

        [ForeignKey("VehicleId")]
        public Vehicle Vehicle { get; set; } = null!;

        [Required]
        [StringLength(500)]
        public string Reason { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Estimated Repair Cost")]
        public decimal EstimatedRepairCost { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Recommended Disposal Value")]
        public decimal RecommendedDisposalValue { get; set; }

        public DisposalRequestStatus Status { get; set; } = DisposalRequestStatus.Pending;

        [StringLength(450)]
        public string? RequestedByUserId { get; set; }

        [StringLength(256)]
        public string? RequestedByEmail { get; set; }

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        [StringLength(450)]
        public string? ReviewedByUserId { get; set; }

        [StringLength(256)]
        public string? ReviewedByEmail { get; set; }

        public DateTime? ReviewedAt { get; set; }

        [StringLength(500)]
        public string? ReviewNotes { get; set; }
    }
}
