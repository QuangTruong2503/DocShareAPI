using DocShareAPI.Data;
using DocShareAPI.DataTransferObject;
using DocShareAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DocShareAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CollectionsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        public CollectionsController(DocShareDbContext context)
        {
            _context = context;
        }

        // 1. Thêm mới một bộ sưu tập
        [HttpPost]
        public async Task<IActionResult> CreateCollection([FromBody] CollectionDTO collection)
        {
            //Kiểm tra token
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Gán giá trị mặc định nếu cần
            var newCollection = new Collections()
            {
                Name = collection.Name,
                Description = collection.Description,
                created_at = DateTime.UtcNow,
                is_public = collection.is_public,
                user_id = decodedToken.userID
            };
            _context.COLLECTIONS.Add(newCollection);
            await _context.SaveChangesAsync();

            return Ok("Tạo bộ sưu tập mới thành công.");
        }

        // 2. Cập nhật một bộ sưu tập
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCollection(int id, [FromBody] Collections updatedCollection)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var collection = await _context.COLLECTIONS.FindAsync(id);
            if (collection == null)
            {
                return NotFound(new { message = $"Collection with ID {id} not found." });
            }

            // Cập nhật các thuộc tính
            collection.Name = updatedCollection.Name;
            collection.Description = updatedCollection.Description;
            collection.is_public = updatedCollection.is_public;

            _context.COLLECTIONS.Update(collection);
            await _context.SaveChangesAsync();

            return Ok(collection);
        }

        // 3. Xóa một bộ sưu tập
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCollection(int id)
        {
            var collection = await _context.COLLECTIONS.FindAsync(id);
            if (collection == null)
            {
                return NotFound(new { message = $"Collection with ID {id} not found." });
            }

            _context.COLLECTIONS.Remove(collection);
            await _context.SaveChangesAsync();

            return NoContent(); // 204 - Xóa thành công
        }

        // 4. Hiển thị danh sách bộ sưu tập theo người dùng
        [HttpGet("my-collection")]
        public async Task<IActionResult> GetCollectionsByUser()
        {
            //Kiểm tra token
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }
            var collections = await _context.COLLECTIONS
                .Where(c => c.user_id == decodedToken.userID)
                .ToListAsync();

            if (collections == null || !collections.Any())
            {
                return NotFound(new { message = $"No collections found for user with ID {decodedToken.userID}." });
            }

            return Ok(collections);
        }
    }
}
