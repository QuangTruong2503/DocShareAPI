using DocShareAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DocShareAPI.Controllers.Public
{
    [Route("api/public")]
    [ApiController]
    public class PublicCategoriesController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        public PublicCategoriesController(DocShareDbContext context)
        {
            _context = context;
        }
        //Lấy danh sách tất cả các category
        [HttpGet("get-all-categories")]
        public async Task<IActionResult> GetAllCategories()
        {
            var categories = await _context.CATEGORIES.Select(c => new { c.category_id, c.Name, c.Description, c.parent_id }).ToListAsync();
            return Ok(categories);
        }
        //Lấy danh sách dựa vào search tên category
        [HttpGet("search-category")]
        public async Task<IActionResult> SearchCategory(string search)
        {
            var categories = await _context.CATEGORIES.Where(c => c.Name.Contains(search)).Select(c => new { c.category_id, c.Name, c.Description, c.parent_id }).ToListAsync();
            return Ok(categories);
        }
        //Lấy danh sách categories dựa vào id
        [HttpGet("get-category-by-id")]
        public async Task<IActionResult> GetCategoryById([FromQuery] string id)
        {
            var category = await _context.CATEGORIES
                .Where(c => c.category_id == id)
                .Select(c => new { c.category_id, c.Name, c.Description, c.parent_id })
                .FirstOrDefaultAsync();

            if (category == null)
            {
                return NotFound();
            }

            return Ok(category);
        }

        [HttpGet("get-category-tree")]
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
    
        public class CategoryDto
        {
            public string category_id { get; set; } = null!;
            public string name { get; set; } = null!;
            public string? description { get; set; }

            public string? parent_id { get; set; }
            public List<CategoryDto> children { get; set; } = new();
        }
    }

}
