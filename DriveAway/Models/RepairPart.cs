using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DriveAway.Models
{
    public class RepairPart
    {
        public int Id { get; set; }

        [Required]  
        public int MaintenanceJobId { get; set; }

        [ForeignKey("MaintenanceJobId")]
        public MaintenanceJob MaintenanceJob { get; set; } = null!;

        [Required]
        [StringLength(200)]
        [Display(Name = "Part Name")]
        public string PartName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        [Display(Name = "Quantity")]
        public string Quantity { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Unit Cost")]
        [Range(0, double.MaxValue)]
        public decimal UnitCost { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Total Cost")]
        [Range(0, double.MaxValue)]
        public decimal TotalCost { get; set; }
    }
}
