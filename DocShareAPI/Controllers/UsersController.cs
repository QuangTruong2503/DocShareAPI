using DocShareAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DocShareAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        public UsersController(DocShareDbContext context)
        {
            _context = context;
        }
        // GET: api/<UsersController>
        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            var list = await _context.USERS.Select(u => new
            {
                u.user_id,
                u.full_name,
                u.Email,
                u.avatar_url,
                u.created_at,
                u.Role,
                u.is_verified
            }).ToListAsync();
            return Ok(list);
        }

        // GET api/<UsersController>/5
        [HttpGet("{id}")]
        public string Get(int id)
        {
            return "value";
        }

        // POST api/<UsersController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<UsersController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<UsersController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
