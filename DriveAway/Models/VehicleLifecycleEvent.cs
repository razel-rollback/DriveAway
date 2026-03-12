using System.ComponentModel.DataAnnotations;

namespace DriveAway.Models
{
    public enum LifecycleEventType
    {
        Acquired,
        Available,
        Rented,
        Returned,
        MaintenanceStart,
        MaintenanceEnd,
        Reserved,
        StatusChanged,
        Retired,
        Disposed,
        BranchAssigned,
        BranchTransferred,
        DisposalRequested,
        DisposalApproved,
        DisposalRejected,
        MaintenanceScheduled,
        MaintenanceCompleted,
        DamageReported,
        RepairAssigned,
        RepairCompleted
    }

    public class VehicleLifecycleEvent
    {
        public int Id { get; set; }

        [Required]
        public int VehicleId { get; set; }

        public Vehicle Vehicle { get; set; } = null!;

        [Required]
        public LifecycleEventType EventType { get; set; }

        [Required]
        public DateTime EventDate { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        public int? Mileage { get; set; }
    }
}
