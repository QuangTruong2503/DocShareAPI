using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class Folders
    {
        [Key]
        public int folder_id { get; set; }
        public Guid owner_user_id { get; set; }
        public int? parent_folder_id { get; set; }
        public required string name { get; set; }
        public string? description { get; set; }
        public string visibility { get; set; } = "private";
        public DateTime created_at { get; set; } = DateTime.UtcNow;
        public DateTime updated_at { get; set; } = DateTime.UtcNow;

        public Users? OwnerUser { get; set; }
        public Folders? ParentFolder { get; set; }
        public ICollection<Folders> ChildFolders { get; set; } = new List<Folders>();
        public ICollection<FolderDocuments> FolderDocuments { get; set; } = new List<FolderDocuments>();
        public ICollection<FolderMembers> FolderMembers { get; set; } = new List<FolderMembers>();
        public ICollection<FolderInvites> FolderInvites { get; set; } = new List<FolderInvites>();
    }
}
