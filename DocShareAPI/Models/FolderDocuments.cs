namespace DocShareAPI.Models
{
    public class FolderDocuments
    {
        public int folder_id { get; set; }
        public int document_id { get; set; }
        public Guid added_by_user_id { get; set; }
        public DateTime added_at { get; set; } = DateTime.UtcNow;

        public Folders? Folder { get; set; }
        public Documents? Document { get; set; }
        public Users? AddedByUser { get; set; }
    }
}
