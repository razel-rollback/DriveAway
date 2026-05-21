using System;
using System.ComponentModel.DataAnnotations;

namespace DriveAway.Models
{
    public class Branch
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Branch Name is required.")]
        [StringLength(100, ErrorMessage = "Branch Name cannot exceed 100 characters.")]
        [RegularExpression(@"^[\p{L}\p{M}\d\s.'\-&]+$", ErrorMessage = "Branch Name contains invalid characters.")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Address is required.")]
        [StringLength(255)]
        public string Address { get; set; }

        [Required(ErrorMessage = "City is required.")]
        [StringLength(100)]
        [RegularExpression(@"^[\p{L}\p{M}\s.'\-]+$", ErrorMessage = "City contains invalid characters.")]
        public string City { get; set; }

        [Required(ErrorMessage = "Contact Number is required.")]
        [Phone(ErrorMessage = "Invalid Contact Number format.")]
        [StringLength(20)]
        [Display(Name = "Contact Number")]
        public string ContactNumber { get; set; }

        [Display(Name = "Active Status")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Date Created")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
    }
}
