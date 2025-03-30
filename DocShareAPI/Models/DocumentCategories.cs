using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class DocumentCategories
    {
        [Key]
        public int document_id { get; set; }
        public required string category_id { get; set; }

        public Documents? Documents { get; set; }
        public Categories? Categories { get; set; }
    }

}
