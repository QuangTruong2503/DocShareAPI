using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DocShareAPI.Models
{
    public class Tokens
    {
        [Key]
        public Guid token_id { get; set; }

        public Guid user_id { get; set; }

        public required string token { get; set; }

        [Column(TypeName = "varchar(20)")]
        public TokenType type { get; set; } // Enum để xác định loại token

        public DateTime expires_at { get; set; } // Thời gian hết hạn

        public DateTime created_at { get; set; }

        public bool is_active { get; set; } // Mặc định token còn hiệu lực

        public string? user_device { get; set; }

        public Users? Users { get; set; }
    }
    // Enum để phân loại token
    public enum TokenType
    {
        Access,
        Refresh,
        EmailVerification,
        PasswordReset,
        TwoFactor
    }
}
