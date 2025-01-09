using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class Follows
    {
        [Key]
        public Guid follower_id { get; set; }
        public Guid following_id { get; set; }
        public DateTime created_at { get; set; } = DateTime.UtcNow;

        public Users Follower { get; set; }
        public Users Following { get; set; }
    }

}
