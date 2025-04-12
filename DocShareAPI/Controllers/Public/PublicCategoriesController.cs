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


    }
}
