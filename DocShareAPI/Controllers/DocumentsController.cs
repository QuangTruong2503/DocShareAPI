using DocShareAPI.Data;
using DocShareAPI.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DocShareAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        public DocumentsController(DocShareDbContext context)
        {
            _context = context;
        }
        
        // GET: api/<DocumentsController>
        [HttpGet("documents")]
        public async Task<IActionResult> GetAllDocuments([FromQuery] PaginationParams paginationParams)
        {
            var query = _context.DOCUMENTS.AsQueryable();

            // Sử dụng extension method ToPagedListAsync
            var pagedData = await query
                .Select(d => new
                {
                    d.document_id,
                    d.Users.full_name,
                    d.Title,
                    d.thumbnail_url,
                    d.like_count,
                    d.is_public
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);
            return Ok(new
            {
                Data = pagedData,
                Pagination = new
                {
                    pagedData.CurrentPage,
                    pagedData.PageSize,
                    pagedData.TotalCount,
                    pagedData.TotalPages
                }
            });
        }

        // GET api/<DocumentsController>/5
        [HttpGet("document/{documentID}")]
        public async Task<IActionResult> GetDocumentByID(int documentID)
        {
            var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentID);
            if (document == null)
            {
                return BadRequest(new
                {
                    message = "Không có dữ liệu Tài liệu"
                });
            }
            return Ok(document);
        }

        // POST api/<DocumentsController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<DocumentsController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<DocumentsController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
