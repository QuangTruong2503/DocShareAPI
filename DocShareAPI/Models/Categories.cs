using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class Categories
    {
        [Key]
        public int category_id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }

        public int? parent_id { get; set; }
        public ICollection<DocumentCategories>? DocumentCategories { get; set; }
    }

}
