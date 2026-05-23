using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class DocumentDownloads
    {
        [Key]
        public long download_id { get; set; }
        public int document_id { get; set; }
        public Guid? user_id { get; set; }
        public string? source { get; set; }
        public string? share_token { get; set; }
        public string? ip_hash { get; set; }
        public DateTime downloaded_at { get; set; } = DateTime.UtcNow;

        public Documents? Document { get; set; }
        public Users? User { get; set; }
    }
}
