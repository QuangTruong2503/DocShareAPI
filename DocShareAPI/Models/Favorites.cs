using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class Favorites
    {
        [Key]
        public int favorite_id { get; set; }
        public Guid user_id { get; set; }
        public int item_id { get; set; }
        public required string item_type { get; set; }
        public DateTime created_at { get; set; } = DateTime.UtcNow;

        public Users? User { get; set; }
    }
}
