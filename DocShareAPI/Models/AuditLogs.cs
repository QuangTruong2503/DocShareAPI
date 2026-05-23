using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class AuditLogs
    {
        [Key]
        public long audit_id { get; set; }
        public Guid? actor_user_id { get; set; }
        public required string action { get; set; }
        public required string entity_type { get; set; }
        public string? entity_id { get; set; }
        public string? metadata { get; set; }
        public string? ip_address { get; set; }
        public DateTime created_at { get; set; } = DateTime.UtcNow;

        public Users? ActorUser { get; set; }
    }
}
