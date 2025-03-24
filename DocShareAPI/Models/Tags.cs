using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class Tags
    {
        [Key]
        public int tag_id { get; set; }
        public required string Name { get; set; }

        public ICollection<DocumentTags>? DocumentTags { get; set; }
    }

}
