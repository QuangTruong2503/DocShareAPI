using DocShareAPI.Data;
using DocShareAPI.DataTransferObject;
using DocShareAPI.Models;
using ELearningAPI.Helpers;
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
        //Khai báo dịch vụ token
        private readonly TokenServices _tokenServices;
        private readonly IConfiguration _configuration;
        public UsersController(DocShareDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
            //Lấy dữ liệu TokenKey từ biến môi trường
            string? tokenScretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
            if (string.IsNullOrEmpty(tokenScretKey))
            {
                Console.WriteLine("Không có khóa token được sử dụng trong LoginController");
                tokenScretKey = _configuration.GetValue<string>("TokenSecretKey");
            }
            if (tokenScretKey != null)
            {
                _tokenServices = new TokenServices(tokenScretKey);
            }
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

        //Login
        [HttpPost("request-login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest loginRequest)
        {
            if (string.IsNullOrEmpty(loginRequest.Email) || string.IsNullOrEmpty(loginRequest.Password))
            {
                return BadRequest(new
                {
                    message = "Email và mật khẩu không được để trống.",
                    isLogin = false
                });
            }

            var user = await _context.USERS
                .Where(u => u.Email == loginRequest.Email)
                .Select(u => new { u.user_id, u.Role, u.password_hash })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                // Không tiết lộ thông tin cụ thể để tránh kẻ tấn công lợi dụng
                return BadRequest(new
                {
                    message = "Email không tồn tại.",
                    isLogin = false
                });
            }
            else if(!PasswordHasher.VerifyPassword(loginRequest.Password, user.password_hash))
            {
                // Không tiết lộ thông tin cụ thể để tránh kẻ tấn công lợi dụng
                return BadRequest(new
                {
                    message = "Mật khẩu không chính xác.",
                    isLogin = false
                });
            }
            var token = _tokenServices.GenerateToken(user.user_id.ToString(), user.Role);

            return Ok(new
            {
                message = "Đăng nhập thành công!",
                success = true,
                token
            });
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
