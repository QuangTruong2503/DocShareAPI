using DocShareAPI.Data;
using DocShareAPI.Models;
using System.Text.Json;

namespace DocShareAPI.Services
{
    public interface IAuditLogService
    {
        Task LogAsync(Guid? actorUserId, string action, string entityType, string? entityId = null, object? metadata = null, string? ipAddress = null);
    }

    public class AuditLogService : IAuditLogService
    {
        private readonly DocShareDbContext _context;

        public AuditLogService(DocShareDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(Guid? actorUserId, string action, string entityType, string? entityId = null, object? metadata = null, string? ipAddress = null)
        {
            _context.AUDIT_LOGS.Add(new AuditLogs
            {
                actor_user_id = actorUserId,
                action = action,
                entity_type = entityType,
                entity_id = entityId,
                metadata = metadata == null ? null : JsonSerializer.Serialize(metadata),
                ip_address = ipAddress,
                created_at = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }
    }
}
