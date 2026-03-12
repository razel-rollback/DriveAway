using System.ComponentModel.DataAnnotations;

namespace DriveAway.Models
{
    public class UserViewModel
    {
        public string Id { get; set; }
        public string Email { get; set; }
        public string UserName { get; set; }
        public IList<string> Roles { get; set; }
        public bool IsBusinessOwner { get; set; } // Flag for disabling actions
        public string BranchName { get; set; }
        public bool IsActive { get; set; }
    }

    public class ArchiveViewModel
    {
        public List<UserViewModel> ArchivedUsers { get; set; } = new();
        public List<CategoryRate> ArchivedCategories { get; set; } = new();
    }

    public class CreateUserViewModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; }

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; }

        [Display(Name = "Role")]
        public string SelectedRole { get; set; }

        [Display(Name = "Branch")]
        public int? BranchId { get; set; }
    }

    public class EditUserViewModel
    {
        public string Id { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Display(Name = "Role")]
        public string SelectedRole { get; set; }

        [Display(Name = "Branch")]
        public int? BranchId { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;
    }
}
