using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class Categories
    {
        [Key]
        public required string category_id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }

        public string? parent_id { get; set; }
        public ICollection<DocumentCategories>? DocumentCategories { get; set; }
    }

}
