using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DriveAway.Models
{
    public class CategoryRate
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        [RegularExpression(@"^[\p{L}\p{M}\d\s.'\-/&]+$", ErrorMessage = "Category contains invalid characters.")]
        public string Category { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Daily Rate (₱)")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Enter a valid daily rate.")]
        public decimal DailyRate { get; set; }

        public bool IsArchived { get; set; }
    }
}
