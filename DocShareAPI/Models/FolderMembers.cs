namespace DocShareAPI.Models
{
    public class FolderMembers
    {
        public int folder_id { get; set; }
        public Guid user_id { get; set; }
        public string role { get; set; } = "viewer";
        public Guid? invited_by_user_id { get; set; }

        public Folders? Folder { get; set; }
        public Users? User { get; set; }
        public Users? InvitedByUser { get; set; }
    }
}
