using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class Categories
    {
        [Key]
        public string category_id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Description { get; set; }

        public string? parent_id { get; set; }

        public ICollection<DocumentCategories> DocumentCategories { get; set; }
            = new List<DocumentCategories>();
    }

}
