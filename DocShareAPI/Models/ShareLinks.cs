using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class ShareLinks
    {
        [Key]
        public int share_link_id { get; set; }
        public required string token { get; set; }
        public Guid owner_user_id { get; set; }
        public int item_id { get; set; }
        public required string item_type { get; set; }
        public string access { get; set; } = "anyone_with_link";
        public string permission { get; set; } = "viewer";
        public bool allow_download { get; set; } = true;
        public string? password_hash { get; set; }
        public DateTime? expires_at { get; set; }
        public int? max_views { get; set; }
        public int? max_downloads { get; set; }
        public int view_count { get; set; }
        public int download_count { get; set; }
        public DateTime created_at { get; set; } = DateTime.UtcNow;
        public DateTime updated_at { get; set; } = DateTime.UtcNow;
        public DateTime? revoked_at { get; set; }

        public Users? OwnerUser { get; set; }
    }
}
