using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class DocumentTags
    {
        [Key]
        public int document_id { get; set; }
        public int tag_id { get; set; }

        public Documents Documents { get; set; }
        public Tags Tags { get; set; }
    }

}
