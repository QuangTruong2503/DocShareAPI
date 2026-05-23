using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class Comments
    {
        [Key]
        public int comment_id { get; set; }
        public int document_id { get; set; }
        public Guid user_id { get; set; }
        public int? parent_comment_id { get; set; }
        public required string content { get; set; }
        public DateTime created_at { get; set; } = DateTime.UtcNow;
        public DateTime updated_at { get; set; } = DateTime.UtcNow;
        public DateTime? deleted_at { get; set; }

        public Documents? Document { get; set; }
        public Users? User { get; set; }
        public Comments? ParentComment { get; set; }
        public ICollection<Comments> Replies { get; set; } = new List<Comments>();
    }
}
