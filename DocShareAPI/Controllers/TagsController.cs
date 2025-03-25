using DocShareAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DocShareAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TagsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        public TagsController(DocShareDbContext context)
        {
            _context = context;
        }
        // GET: api/<TagsController>
        [HttpGet("public/get-all-tags")]
        public async Task<IActionResult> GetAllTags()
        {
            var tags = await _context.TAGS.Select(t => new {t.tag_id, t.Name}).ToListAsync();
            if (tags == null)
            {
                return NotFound();
            }
            return Ok(tags);
        }
        //Lấy danh sách dựa vào search tên category
        [HttpGet("public/search-tags")]
        public async Task<IActionResult> SearchTags(string search)
        {
            var categories = await _context.TAGS.Where(c => c.Name.Contains(search)).Select(c => new { c.tag_id, c.Name }).ToListAsync();
            return Ok(categories);
        }

    }
}
