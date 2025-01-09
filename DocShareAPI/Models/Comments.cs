using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class Comments
    {
        [Key]
        public int comment_id { get; set; }
        public int document_id { get; set; }
        public Guid user_id { get; set; }
        public string Content { get; set; }
        public DateTime created_at { get; set; } = DateTime.UtcNow;
        public Documents Documents { get; set; }
        public Users Users { get; set; }
    }

}
