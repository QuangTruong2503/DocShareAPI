using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
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
        //Khai báo CLoudinary
        private readonly Cloudinary _cloudinary;
        private readonly ILogger<UsersController> _logger;


        public UsersController(DocShareDbContext context, IConfiguration configuration, ILogger<UsersController> logger)
        {
            _logger = logger;
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
            try
            {
                var cloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME")
                    ?? configuration["Cloudinary:CloudName"];
                var apiKey = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY")
                    ?? configuration["Cloudinary:ApiKey"];
                var apiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET")
                    ?? configuration["Cloudinary:ApiSecret"];

                if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
                {
                    throw new ArgumentNullException("Cloudinary configuration is incomplete");
                }

                _cloudinary = new Cloudinary(new Account(cloudName, apiKey, apiSecret));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initializing Cloudinary: {ex}");
                throw;
            }

           
        }
        // GET: api/<UsersController>
        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            var list = await _context.USERS.Select(u => new
            {
                u.user_id,
                u.Username,
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
        [HttpGet("my-profile")]
        public async Task<IActionResult> GetMyProfile(Guid userID)
        {
            var user = await _context.USERS.Where(u => u.user_id == userID)
                .Select(u => new { u.user_id, u.Username , u.full_name, u.Email, u.avatar_url, u.created_at, u.Role, u.is_verified})
                .FirstOrDefaultAsync();
            if (user == null)
            {
                return BadRequest(new
                {
                    message = "Không tìm thấy dữ liệu người dùng.",
                    success = false
                });
            }
            return Ok(user);
        }

        [HttpGet("verify-token/{token}")]
        public IActionResult CheckToken(string token)
        {
            var decode = _tokenServices.DecodeToken(token);
            if (decode == null)
            {
                return Ok(new
                {
                    message = "Token không hợp lệ",
                    success = false,
                });
            }
            return Ok(new
            {
                success = true,
                message = "Token hợp lệ",
                data = decode
            });
        }

        //Login
        [HttpPost("request-login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest loginRequest)
        {
            if (string.IsNullOrEmpty(loginRequest.Email) || string.IsNullOrEmpty(loginRequest.Password))
            {
                return Ok(new
                {
                    message = "Email và mật khẩu không được để trống.",
                    isLogin = false
                });
            }

            try
            {
                var user = await _context.USERS
                .Where(u => u.Email == loginRequest.Email || u.Username == loginRequest.Email)
                .FirstOrDefaultAsync();

                if (user == null)
                {
                    // Không tiết lộ thông tin cụ thể để tránh kẻ tấn công lợi dụng
                    return Ok(new
                    {
                        message = "Tài khoản hoặc email không tồn tại.",
                        isLogin = false
                    });
                }
                else if (!PasswordHasher.VerifyPassword(loginRequest.Password, user.password_hash))
                {
                    // Không tiết lộ thông tin cụ thể để tránh kẻ tấn công lợi dụng
                    return Ok(new
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
                    token,
                    user = new {Email = user.Email, FullName = user.full_name, AvatarUrl = user.avatar_url}
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }


        //Register / Đăng ký thành viên
        [HttpPost("request-register")]
        public async Task<IActionResult> Register([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new
                {
                    message = "Email và password không được để trống",
                    success = false
                });
            }
            try
            {
                if (await _context.USERS.AnyAsync(u => u.Email.Equals(request.Email)))
                {
                    return Ok(new
                    {
                        message = "Email này đã được sử dụng",
                        success = false
                    });
                }
                var newUser = new Users
                {
                    user_id = Guid.NewGuid(),
                    Username = request.Email.Split('@')[0],
                    Email = request.Email,
                    password_hash = PasswordHasher.HashPassword(request.Password)
                };

                _context.USERS.Add(newUser);
                await _context.SaveChangesAsync();
                return Ok(new
                {
                    message = "Đăng ký thành công",
                    success = true
                });
            }
            catch (Exception ex)
            {
                return BadRequest($"Lỗi khi đăng ký tài khoản {ex.Message}");
            }
           
        }

        //Cập nhật hình ảnh
        [HttpPut("update-image")]
        public async Task<IActionResult> UpdateImage(IFormFile image, Guid userID)
        {
            try
            {
                //Tìm userID
                var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == userID);
                if (user == null)
                {
                    return BadRequest(new
                    {
                        message = "Không tìm thấy người dùng",
                        success = false
                    });
                }
                if (image == null || image.Length == 0)
                {
                    _logger.LogWarning("Image upload attempted with null or empty image");
                    return BadRequest("No image uploaded or image is empty");
                }

                using (var stream = image.OpenReadStream())
                {
                    var uploadParams = new ImageUploadParams
                    {
                        File = new FileDescription(image.FileName, stream),
                        Folder = "DocShare/users",
                        Transformation = new Transformation()
                            .Width(500)
                            .Height(500)
                            .Crop("fill")
                    };

                    var uploadResult = await _cloudinary.UploadAsync(uploadParams);

                    //Cập nhật hình ảnh
                    user.avatar_url = uploadResult.SecureUrl.ToString();
                    _context.USERS.Update(user);
                    await _context.SaveChangesAsync();
                    return Ok(new
                    {
                        message = "Cập nhật ảnh đại diện thành công!",
                        success = true,
                        user = new { Email = user.Email, FullName = user.full_name, AvatarUrl = user.avatar_url }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error uploading image: {ex.Message}");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        //Cập nhật thông tin
        [HttpPut("update-user")]
        public async Task<IActionResult> UpdateUser([FromBody] UserUpdateDTO userDTO)
        {
            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == userDTO.user_id);
            var duplicate = await _context.USERS.AnyAsync(u => u.Username == userDTO.username || u.Email == userDTO.email);
            if (user == null)
            {
                return BadRequest(new
                {
                    message = "Không tìm thấy người dùng",
                    success = false
                });
            }
            // Kiểm tra trùng lặp (loại trừ bản ghi hiện tại)
            var isDuplicateUserName = await _context.USERS.AnyAsync(u =>

                u.user_id != userDTO.user_id && u.Username == userDTO.username);

            var isDuplicateEmail = await _context.USERS.AnyAsync(u =>

                u.user_id != userDTO.user_id && u.Email == userDTO.email);
            if (isDuplicateUserName)
            {
                return Ok(new
                {
                    message = "Tên đăng nhập đã tồn tại",
                    success = false
                });
            }
            if (isDuplicateEmail)
            {
                return Ok(new
                {
                    message = "Email đã tồn tại",
                    success = false
                });
            }
            //Cập nhật dữ liệu
            user.full_name = userDTO.full_name;
            user.Email = userDTO.email;
            user.Username = userDTO.username;
            _context.USERS.Update(user);
            await _context.SaveChangesAsync();
            return Ok(new
            {
                message = "Cập nhật thông tin thành công!",
                success = true
            });
        }

        // DELETE api/<UsersController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }

    }
}
