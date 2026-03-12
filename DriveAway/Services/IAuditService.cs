namespace DriveAway.Services
{
    public interface IAuditService
    {
        Task LogAsync(
            string action,
            string module,
            string? entityType = null,
            string? entityId = null,
            string? entityName = null,
            string? details = null,
            string? userEmailOverride = null);
    }
}
