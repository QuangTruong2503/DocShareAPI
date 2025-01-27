using System.ComponentModel.DataAnnotations;
using System.Reflection.Metadata;

namespace DocShareAPI.Models
{
    public class CollectionDocuments
    {
        [Key]
        public int collection_id { get; set; }
        public int document_id { get; set; }
        public DateTime added_at { get; set; } = DateTime.UtcNow;

        public Collections? Collections { get; set; }
        public Documents? Documents { get; set; }
    }

}
