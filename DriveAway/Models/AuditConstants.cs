namespace DriveAway.Models
{
    public static class AuditAction
    {
        public const string Create             = "Create";
        public const string Update             = "Update";
        public const string Delete             = "Delete";
        public const string Archive            = "Archive";
        public const string Login              = "Login";
        public const string LoginFailed        = "Login Failed";
        public const string Logout             = "Logout";
        public const string StatusChanged      = "Status Changed";
        public const string CheckOut           = "Check-Out";
        public const string CheckIn            = "Check-In";
        public const string Assign             = "Assign";
        public const string Transfer           = "Transfer";
        public const string DisposalRequest    = "Disposal Request";
        public const string DisposalApprove    = "Disposal Approve";
        public const string DisposalReject     = "Disposal Reject";
        public const string ScheduleMaintenance = "Schedule Maintenance";
        public const string CompleteMaintenance = "Complete Maintenance";
        public const string DamageReport       = "Damage Report";
        public const string AssignMechanic     = "Assign Mechanic";
        public const string CreateMaintenanceJob = "Create Maintenance Job";
        public const string StartRepair        = "Start Repair";
        public const string RepairComplete     = "Repair Complete";
    }

    public static class AuditModule
    {
        public const string Asset              = "Asset";
        public const string UserManagement     = "User Management";
        public const string Authentication     = "Authentication";
        public const string RoleManagement     = "Role Management";
        public const string Rental             = "Rental Operations";
        public const string CategoryManagement = "Category Management";
        public const string BranchManagement   = "Branch Management";
        public const string Disposal           = "Disposal";
        public const string Maintenance        = "Maintenance";
        public const string MaintenanceJobs    = "Maintenance Jobs";
    }
}
