using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class DocumentVersions
    {
        [Key]
        public int version_id { get; set; }
        public int document_id { get; set; }
        public int version_number { get; set; }
        public required string file_url { get; set; }
        public required string public_id { get; set; }
        public required string asset_id { get; set; }
        public int file_size { get; set; }
        public int pages { get; set; }
        public Guid uploaded_by { get; set; }
        public string? change_note { get; set; }
        public DateTime created_at { get; set; } = DateTime.UtcNow;

        public Documents? Document { get; set; }
        public Users? UploadedByUser { get; set; }
    }
}
