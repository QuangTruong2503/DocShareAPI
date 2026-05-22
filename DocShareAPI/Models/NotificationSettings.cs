using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class NotificationSettings
    {
        [Key]
        public Guid user_id { get; set; }
        public bool in_app_enabled { get; set; } = true;
        public bool email_on_comment { get; set; } = true;
        public bool email_on_follow { get; set; } = true;
        public bool email_on_folder_invite { get; set; } = true;
        public bool email_on_report_update { get; set; } = true;
        public DateTime updated_at { get; set; } = DateTime.UtcNow;

        public Users? User { get; set; }
    }
}
