using System.ComponentModel.DataAnnotations;
using System.Reflection.Metadata;

namespace DocShareAPI.Models
{
    public class Reports
    {
        [Key]
        public int report_id { get; set; }
        public Guid user_id { get; set; }
        public int document_id { get; set; }
        public string Reason { get; set; }
        public string Status { get; set; } = "Chờ giải quyết";
        public DateTime created_at { get; set; } = DateTime.UtcNow;
        public Users Users { get; set; }
        public Documents Documents { get; set; }
    }

}
