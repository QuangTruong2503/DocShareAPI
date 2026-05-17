using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class Notifications
    {
        [Key]
        public int notification_id { get; set; }
        public Guid recipient_user_id { get; set; }
        public Guid? actor_user_id { get; set; }
        public required string type { get; set; }
        public required string title { get; set; }
        public string? message { get; set; }
        public int? related_document_id { get; set; }
        public int? related_comment_id { get; set; }
        public int? related_report_id { get; set; }
        public string? target_url { get; set; }
        public string? metadata { get; set; }
        public bool is_read { get; set; } = false;
        public DateTime? read_at { get; set; }
        public DateTime created_at { get; set; } = DateTime.UtcNow;
        public DateTime updated_at { get; set; } = DateTime.UtcNow;

        public Users? RecipientUser { get; set; }
        public Users? ActorUser { get; set; }
        public Documents? RelatedDocument { get; set; }
        public Reports? RelatedReport { get; set; }
    }
}
