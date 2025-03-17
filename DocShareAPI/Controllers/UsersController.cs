using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using DocShareAPI.Data;
using DocShareAPI.DataTransferObject;
using DocShareAPI.Models;
using DocShareAPI.Services;
using ELearningAPI.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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
        //Khai báo CLoudinary
        private readonly ILogger<UsersController> _logger;
        private readonly ICloudinaryService _cloudinaryService;

        public UsersController(DocShareDbContext context, ICloudinaryService cloudinaryService, ILogger<UsersController> logger, TokenServices tokenServices)
        {
            _logger = logger;
            _context = context;
            _cloudinaryService = cloudinaryService;
            _tokenServices = tokenServices;
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
        public async Task<IActionResult> GetMyProfile()
        {
            //Kiểm tra tính hợp lệ của token
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized(); // Không cần nữa vì middleware đã xử lý
            }
            var user = await _context.USERS.Where(u => u.user_id == decodedTokenResponse.userID)
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
            if (user.user_id != decodedTokenResponse.userID && decodedTokenResponse.roleID != "admin")
            {
                return BadRequest("Bạn không thể xem hoặc không đủ quyền hạn để xem dữ liệu người dùng này!");
            }
            return Ok(user);
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
                .FirstOrDefaultAsync(u => u.Email == loginRequest.Email || u.Username == loginRequest.Email);

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
        public async Task<IActionResult> UpdateImage(IFormFile image)
        {
            //Kiểm tra token
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }
            try
            {
                //Tìm userID
                var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == decodedToken.userID);
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
                        Folder = $"DocShare/users/{user.Username}",
                        Transformation = new Transformation()
                            .Width(500)
                            .Height(500)
                            .Crop("fill")
                    };

                    var uploadResult = await _cloudinaryService.Cloudinary.UploadAsync(uploadParams);

                    //Cập nhật hình ảnh
                    user.avatar_url = uploadResult.SecureUrl.ToString();
                    _context.USERS.Update(user);
                    await _context.SaveChangesAsync();
                    return Ok(new
                    {
                        message = "Cập nhật ảnh đại diện thành công!",
                        success = true,
                        user = new { email = user.Email, fullName = user.full_name, avatarUrl = user.avatar_url }
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
            //Kiểm tra token

            //Kiểm tra token
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }
            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == userDTO.user_id);
            var duplicate = await _context.USERS.AnyAsync(u => u.Email == userDTO.email);
            if (user == null)
            {
                return BadRequest(new
                {
                    message = "Không tìm thấy người dùng",
                    success = false
                });
            }
            var isDuplicateUserName = await _context.USERS.AnyAsync(u =>

                u.user_id != userDTO.user_id && u.Username == userDTO.Username);
            if (isDuplicateUserName)
            {
                return Ok(new
                {
                    message = "UserName đã tồn tại",
                    success = false
                });
            }

            var isDuplicateEmail = await _context.USERS.AnyAsync(u =>

                u.user_id != userDTO.user_id && u.Email == userDTO.email);
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
            user.Username = userDTO.Username;
            _context.USERS.Update(user);
            await _context.SaveChangesAsync();
            return Ok(new
            {
                message = "Cập nhật thông tin thành công!",
                success = true,
                user = new { email = user.Email, fullName = user.full_name, avatarUrl = user.avatar_url }
            });
        }

        // DELETE api/<UsersController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
