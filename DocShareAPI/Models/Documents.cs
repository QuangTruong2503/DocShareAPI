using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DocShareAPI.Models
{
    public class Documents
    {
        [Key]
        public int document_id { get; set; }
        public Guid user_id { get; set; }
        public required string Title { get; set; }
        public string? Description { get; set; }
        public required string file_url { get; set; }
        public required string thumbnail_url { get; set; }
        public int download_count { get; set; } = 0;
        public int like_count { get; set; } = 0;
        public string Status { get; set; } = "active";
        public DateTime uploaded_at { get; set; } = DateTime.UtcNow;
        public string? file_type { get; set; }
        public int file_size { get; set; }
        public bool is_public { get; set; } = true;

        public Users? Users { get; set; }
        public ICollection<DocumentCategories>? DocumentCategories { get; set; }
        public ICollection<DocumentTags>? DocumentTags { get; set; }
        public ICollection<Comments>? Comments { get; set; }
        public ICollection<CollectionDocuments>? CollectionDocuments { get; set; }
    }

}
