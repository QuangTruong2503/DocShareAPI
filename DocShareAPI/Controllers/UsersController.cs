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
        private readonly TokenServices _tokenServices;
        private readonly ILogger<UsersController> _logger;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly IHttpClientFactory _httpClientFactory;

        public UsersController(DocShareDbContext context, ICloudinaryService cloudinaryService, ILogger<UsersController> logger, TokenServices tokenServices, IHttpClientFactory httpClientFactory )
        {
            _logger = logger;
            _context = context;
            _cloudinaryService = cloudinaryService;
            _tokenServices = tokenServices;
            _httpClientFactory = httpClientFactory;
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
        [HttpPost("public/request-login")]
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
                    return Ok(new
                    {
                        message = "Tài khoản hoặc email không tồn tại.",
                        isLogin = false
                    });
                }

                if (!PasswordHasher.VerifyPassword(loginRequest.Password, user.password_hash))
                {
                    return Ok(new
                    {
                        message = "Mật khẩu không chính xác.",
                        isLogin = false
                    });
                }

                // Tạo token
                var token = _tokenServices.GenerateToken(user.user_id.ToString(), user.Role);

                // Thêm token vào bảng Tokens
                var tokenEntity = new Tokens
                {
                    token_id = Guid.NewGuid(),
                    user_id = user.user_id,
                    token = token,
                    type = TokenType.Access,
                    expires_at = DateTime.UtcNow.AddDays(3),
                    is_active = true,
                    created_at = DateTime.UtcNow
                };

                try
                {
                    _context.TOKENS.Add(tokenEntity);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException dbEx)
                {
                    // Xử lý lỗi cụ thể từ database
                    var innerException = dbEx.InnerException?.Message ?? dbEx.Message;
                    return StatusCode(500, new
                    {
                        message = "Lỗi khi lưu token vào database",
                        error = innerException,
                        success = false
                    });
                }
                catch (Exception ex)
                {
                    // Xử lý các lỗi khác liên quan đến database
                    return StatusCode(500, new
                    {
                        message = "Lỗi không xác định khi lưu token",
                        error = ex.Message,
                        success = false
                    });
                }

                return Ok(new
                {
                    message = "Đăng nhập thành công!",
                    success = true,
                    token,
                    user = new
                    {
                        Email = user.Email,
                        FullName = user.full_name,
                        AvatarUrl = user.avatar_url
                    }
                });
            }
            catch (Exception ex)
            {
                // Lỗi ngoài quá trình lưu database
                return StatusCode(500, new
                {
                    message = "Lỗi trong quá trình đăng nhập",
                    error = ex.Message,
                    success = false
                });
            }
        }

        //Login with Google
        [HttpPost("public/request-login-google")]
        public async Task<IActionResult> GoogleLogin([FromBody] string AccessToken)
        {
            try
            {
                // Gọi Google User Info API với access token
                var userInfo = await GetUserInfoFromGoogle(AccessToken);
                if (userInfo == null)
                {
                    return BadRequest(new { Message = "Không thể lấy thông tin người dùng từ Google" });
                }
                var user = await _context.USERS.FirstOrDefaultAsync(u => u.Email == userInfo.email);
                if (user == null)
                {
                    // Tạo mới user
                    user = new Users
                    {
                        user_id = Guid.NewGuid(),
                        Email = userInfo.email,
                        Username = userInfo.email.Split('@')[0],
                        full_name = userInfo.name,
                        avatar_url = userInfo.picture,
                        Role = "user",
                        password_hash = PasswordHasher.HashPassword(Guid.NewGuid().ToString())
                    };
                    _context.USERS.Add(user);
                    await _context.SaveChangesAsync();
                }
                // Tạo token
                var token = _tokenServices.GenerateToken(user.user_id.ToString(), user.Role);
                // Thêm token vào bảng Tokens
                var tokenEntity = new Tokens
                {
                    token_id = Guid.NewGuid(),
                    user_id = user.user_id,
                    token = token,
                    type = TokenType.Access,
                    expires_at = DateTime.UtcNow.AddDays(3),
                    is_active = true,
                    created_at = DateTime.UtcNow
                };

                try
                {
                    _context.TOKENS.Add(tokenEntity);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException dbEx)
                {
                    // Xử lý lỗi cụ thể từ database
                    var innerException = dbEx.InnerException?.Message ?? dbEx.Message;
                    return StatusCode(500, new
                    {
                        message = "Lỗi khi lưu token vào database",
                        error = innerException,
                        success = false
                    });
                }
                catch (Exception ex)
                {
                    // Xử lý các lỗi khác liên quan đến database
                    return StatusCode(500, new
                    {
                        message = "Lỗi không xác định khi lưu token",
                        error = ex.Message,
                        success = false
                    });
                }
                return Ok(new
                {
                    message = "Đăng nhập thành công!",
                    success = true,
                    token,
                    user = new
                    {
                        Email = user.Email,
                        FullName = user.full_name,
                        AvatarUrl = user.avatar_url
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = "Access token không hợp lệ", Error = ex.Message });
            }
        }

        [HttpPost("request-logout")]
        public async Task<IActionResult> Logout([FromBody] string token)
        {
            //Kiểm tra token
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }
            var tokenEntity = await _context.TOKENS.FirstOrDefaultAsync(t => t.token == token);
            if (tokenEntity == null)
            {
                return Ok(new
                {
                    message = "Token không tồn tại",
                    success = false
                });
            }
            await Task.Run(() =>
            {
                _context.TOKENS.Remove(tokenEntity);
                _context.SaveChanges();
            });

            return Ok(new
            {
                message = "Đăng xuất thành công",
                success = true
            });
        }

        //Register / Đăng ký thành viên
        [HttpPost("public/request-register")]
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
                    try
                    {
                        
                        var uploadParams = new ImageUploadParams
                        {
                            File = new FileDescription(image.FileName, stream),
                            Folder = $"DocShare/users/{user.Username}",
                            Transformation = new Transformation()
                            .Width(300)
                            .Height(300)
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
                    catch (Exception ex)
                    {
                        return StatusCode(500, $"Lỗi khi lấy assetId: {ex.Message}");
                    }
                    
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

        private async Task<GoogleUserInfo> GetUserInfoFromGoogle(string accessToken)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.GetAsync("https://www.googleapis.com/oauth2/v3/userinfo");
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync();
            var userInfo = JsonSerializer.Deserialize<GoogleUserInfo>(jsonString);
            if (userInfo == null)
            {
                throw new Exception("Không thể lấy thông tin người dùng từ Google");
            }
            return userInfo;
        }
    }
    public class GoogleUserInfo
    {
        public required string sub { get; set; }
        public required string email { get; set; }
        public required bool email_verified { get; set; }
        public required string name { get; set; }
        public required string picture { get; set; }
    }
}
