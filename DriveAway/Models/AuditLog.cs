using System.ComponentModel.DataAnnotations;

namespace DriveAway.Models
{
    public class AuditLog
    {
        public int Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [StringLength(450)]
        public string? UserId { get; set; }

        [StringLength(256)]
        public string? UserEmail { get; set; }

        [Required, StringLength(100)]
        public string Action { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string Module { get; set; } = string.Empty;

        [StringLength(100)]
        public string? EntityType { get; set; }

        [StringLength(450)]
        public string? EntityId { get; set; }

        [StringLength(200)]
        public string? EntityName { get; set; }

        [StringLength(1000)]
        public string? Details { get; set; }

        [StringLength(50)]
        public string? IpAddress { get; set; }
    }
}
