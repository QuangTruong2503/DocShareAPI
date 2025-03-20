using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class Collections
    {
        [Key]
        public int collection_id { get; set; } 
        public Guid user_id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public bool is_public { get; set; } = true;
        public DateTime created_at { get; set; } = DateTime.UtcNow;

        public Users? Users { get; set; }
        public ICollection<CollectionDocuments>? CollectionDocuments { get; set; }
    }

}
