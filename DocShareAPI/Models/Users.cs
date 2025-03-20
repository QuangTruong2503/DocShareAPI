using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection.Metadata;

namespace DocShareAPI.Models
{
    public class Users
    {
        [Key]
        public Guid user_id { get; set; }
        public required string Username { get; set; }
        public required string Email { get; set; }
        public required string password_hash { get; set; }
        public string? full_name { get; set; }
        public string avatar_url { get; set; } = "https://res.cloudinary.com/brandocloud/image/upload/v1736401991/DocShare/users/default-avt.png";
        public DateTime created_at { get; set; } = DateTime.UtcNow;
        public string Role { get; set; } = "user";
        public bool is_verified { get; set; } = false;

        public ICollection<Tokens>? Tokens { get; set; }
        public ICollection<Likes>? Likes { get; set; }
        public ICollection<Documents>? Documents { get; set; }
        public ICollection<Comments>? Comments { get; set; }
        public ICollection<Collections>? Collections { get; set; }
        public ICollection<Follows>? Followers { get; set; }
        public ICollection<Follows>? Following { get; set; }
    }

}
