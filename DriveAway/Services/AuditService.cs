using DriveAway.Data;
using DriveAway.Models;
using System.Security.Claims;

namespace DriveAway.Services
{
    public class AuditService : IAuditService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogAsync(
            string action,
            string module,
            string? entityType = null,
            string? entityId = null,
            string? entityName = null,
            string? details = null,
            string? userEmailOverride = null)
        {
            var http = _httpContextAccessor.HttpContext;

            var userId    = http?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            var userEmail = userEmailOverride
                         ?? http?.User?.FindFirstValue(ClaimTypes.Email)
                         ?? http?.User?.Identity?.Name;

            var ip = http?.Connection?.RemoteIpAddress?.ToString();

            _context.AuditLogs.Add(new AuditLog
            {
                Timestamp  = DateTime.UtcNow,
                UserId     = userId,
                UserEmail  = userEmail,
                Action     = action,
                Module     = module,
                EntityType = entityType,
                EntityId   = entityId,
                EntityName = entityName,
                Details    = details,
                IpAddress  = ip
            });

            await _context.SaveChangesAsync();
        }
    }
}
