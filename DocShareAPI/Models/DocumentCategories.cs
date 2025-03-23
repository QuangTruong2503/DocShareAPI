using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class DocumentCategories
    {
        [Key]
        public int document_id { get; set; }
        public int category_id { get; set; }

        public Documents? Documents { get; set; }
        public Categories? Categories { get; set; }
    }

}
