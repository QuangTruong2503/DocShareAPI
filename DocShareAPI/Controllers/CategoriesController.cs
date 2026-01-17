using DocShareAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DocShareAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoriesController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        public CategoriesController(DocShareDbContext context)
        {
            _context = context;
        }
        [HttpGet("public/get-category-tree")]
        public async Task<IActionResult> GetCategoryTree()
        {
            var categories = await _context.CATEGORIES
                .Select(c => new CategoryDto
                {
                    category_id = c.category_id,
                    name = c.Name,
                    description = c.Description,
                    parent_id = c.parent_id
                })
                .ToListAsync();

            var lookup = categories.ToDictionary(c => c.category_id);

            var roots = new List<CategoryDto>();

            foreach (var category in categories)
            {
                if (!string.IsNullOrEmpty(category.parent_id)
                    && lookup.ContainsKey(category.parent_id))
                {
                    lookup[category.parent_id].children.Add(category);
                }
                else
                {
                    roots.Add(category);
                }
            }

            return Ok(roots);
        }
    }
    public class CategoryDto
    {
        public string category_id { get; set; } = null!;
        public string name { get; set; } = null!;
        public string? description { get; set; }

        public string? parent_id { get; set; }
        public List<CategoryDto> children { get; set; } = new();
    }
}
