using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class DocumentCategories
    {
        public int document_id { get; set; }
        public string category_id { get; set; } = null!;

        public Documents Documents { get; set; } = null!;
        public Categories Categories { get; set; } = null!;
    }

}
