using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class FolderInvites
    {
        [Key]
        public int invite_id { get; set; }
        public int folder_id { get; set; }
        public Guid inviter_user_id { get; set; }
        public Guid? invitee_user_id { get; set; }
        public string? invitee_email { get; set; }
        public string role { get; set; } = "viewer";
        public string status { get; set; } = "pending";
        public required string token { get; set; }
        public DateTime? expires_at { get; set; }
        public DateTime created_at { get; set; } = DateTime.UtcNow;
        public DateTime updated_at { get; set; } = DateTime.UtcNow;

        public Folders? Folder { get; set; }
        public Users? InviterUser { get; set; }
        public Users? InviteeUser { get; set; }
    }
}
