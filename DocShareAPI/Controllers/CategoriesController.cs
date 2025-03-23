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
        //Lấy danh sách tất cả các category
        [HttpGet("public/get-all-categories")]
        public async Task<IActionResult> GetAllCategories()
        {
            var categories = await _context.CATEGORIES.Select(c => new {c.category_id, c.Name, c.Description, c.parent_id}).ToListAsync();
            return Ok(categories);
        }
        //Lấy danh sách dựa vào search tên category
        [HttpGet("public/search-category")]
        public async Task<IActionResult> SearchCategory(string search)
        {
            var categories = await _context.CATEGORIES.Where(c => c.Name.Contains(search)).Select(c => new { c.category_id, c.Name, c.Description, c.parent_id }).ToListAsync();
            return Ok(categories);
        }
    }
}
