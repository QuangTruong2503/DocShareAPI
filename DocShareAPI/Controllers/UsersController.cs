using Autofac.Core;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using DocShareAPI.Data;
using DocShareAPI.DataTransferObject;
using DocShareAPI.Helpers;
using DocShareAPI.Models;
using DocShareAPI.Services;
using DocShareAPI.Services.EmailServices;
using ELearningAPI.Helpers;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text;

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
        private readonly IConfiguration _config;
        private readonly IDistributedCache _cache;
        private readonly ITwoFactorEmailService _emailService;

        public UsersController(
            DocShareDbContext context,
            ICloudinaryService cloudinaryService,
            ILogger<UsersController> logger,
            TokenServices tokenServices,
            IConfiguration configuration,
            IDistributedCache cache,
            ITwoFactorEmailService emailService)
        {
            _logger = logger;
            _context = context;
            _cloudinaryService = cloudinaryService;
            _tokenServices = tokenServices;
            _config = configuration;
            _cache = cache;
            _emailService = emailService;
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
                .Select(u => new { u.user_id, u.Username, u.full_name, u.Email, u.avatar_url, u.created_at, u.Role, u.is_verified })
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
        //Login with 2FA Check
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

                // ===== KIỂM TRA 2FA =====
                if (user.two_factor_enabled)
                {
                    // Tạo temporary token để verify 2FA
                    var tempToken = _tokenServices.GenerateToken(user.user_id.ToString(), user.Role);
                    var hashedTempToken = TokenHasher.HashToken(tempToken);

                    // Lưu temp token vào database
                    var tempTokenEntity = new Tokens
                    {
                        token_id = Guid.NewGuid(),
                        user_id = user.user_id,
                        token = hashedTempToken,
                        type = TokenType.TwoFactor, // Cần thêm enum value này
                        expires_at = DateTime.UtcNow.AddMinutes(3), // Token 2FA chỉ tồn tại 03 phút
                        is_active = true,
                        created_at = DateTime.UtcNow,
                        user_device = loginRequest.UserDevice
                    };

                    _context.TOKENS.Add(tempTokenEntity);
                    await _context.SaveChangesAsync();

                    // Gửi mã 2FA (tùy vào phương thức)
                    string twoFactorCode = GenerateRandomCode.GenerateTwoFactorCode(); // Tạo mã 6 số
                    await SendTwoFactorCode(user, twoFactorCode); // Gửi qua email/SMS/app

                    // Lưu mã 2FA vào cache hoặc database (có thời hạn)
                    await SaveTwoFactorCode(user.user_id, twoFactorCode);

                    return Ok(new
                    {
                        message = "Yêu cầu xác thực 2FA",
                        isLogin = false,
                        require2FA = true,
                        twoFactorMethod = user.two_factor_method.ToString(),
                        tempToken = tempToken, // Token tạm để verify 2FA
                        maskedContact = GetMaskedContact(user) // Ẩn một phần email/số điện thoại
                    });
                }

                // ===== ĐĂNG NHẬP BÌNH THƯỜNG (KHÔNG CÓ 2FA) =====
                var token = _tokenServices.GenerateToken(user.user_id.ToString(), user.Role);
                var hashedToken = TokenHasher.HashToken(token);

                var tokenEntity = new Tokens
                {
                    token_id = Guid.NewGuid(),
                    user_id = user.user_id,
                    token = hashedToken,
                    type = TokenType.Access,
                    expires_at = DateTime.UtcNow.AddDays(3),
                    is_active = true,
                    created_at = DateTime.UtcNow,
                    user_device = loginRequest.UserDevice
                };

                try
                {
                    _context.TOKENS.Add(tokenEntity);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException dbEx)
                {
                    var innerException = dbEx.InnerException?.Message ?? dbEx.Message;
                    return StatusCode(500, new
                    {
                        message = "Lỗi khi lưu token vào database",
                        error = innerException,
                        success = false
                    });
                }

                return Ok(new
                {
                    message = "Đăng nhập thành công!",
                    success = true,
                    isLogin = true,
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
                return StatusCode(500, new
                {
                    message = "Lỗi trong quá trình đăng nhập",
                    error = ex.Message,
                    success = false
                });
            }
        }

        // ===== ENDPOINT XÁC NHẬN 2FA =====
        [HttpPost("public/verify-2fa")]
        public async Task<IActionResult> Verify2FA([FromBody] Verify2FARequest request)
        {
            if (string.IsNullOrEmpty(request.TempToken) || string.IsNullOrEmpty(request.Code))
            {
                return Ok(new
                {
                    message = "Thông tin xác thực không hợp lệ",
                    success = false
                });
            }

            try
            {
                // Verify temp token
                var hashedTempToken = TokenHasher.HashToken(request.TempToken);
                var tempTokenEntity = await _context.TOKENS
                    .Include(t => t.Users)
                    .FirstOrDefaultAsync(t =>
                        t.token == hashedTempToken &&
                        t.type == TokenType.TwoFactor &&
                        t.is_active &&
                        t.expires_at > DateTime.UtcNow);

                if (tempTokenEntity == null)
                {
                    return Ok(new
                    {
                        message = "Token xác thực không hợp lệ hoặc đã hết hạn",
                        success = false
                    });
                }

                // Verify 2FA code
                var isValidCode = await VerifyTwoFactorCode(tempTokenEntity.user_id, request.Code);
                if (!isValidCode)
                {
                    return Ok(new
                    {
                        message = "Mã xác thực không chính xác",
                        success = false
                    });
                }

                // Vô hiệu hóa temp token
                tempTokenEntity.is_active = false;

                // Tạo access token chính thức
                var user = tempTokenEntity.Users;
                var accessToken = _tokenServices.GenerateToken(user.user_id.ToString(), user.Role);
                var hashedAccessToken = TokenHasher.HashToken(accessToken);

                var accessTokenEntity = new Tokens
                {
                    token_id = Guid.NewGuid(),
                    user_id = user.user_id,
                    token = hashedAccessToken,
                    type = TokenType.Access,
                    expires_at = DateTime.UtcNow.AddDays(3),
                    is_active = true,
                    created_at = DateTime.UtcNow
                };

                _context.TOKENS.Add(accessTokenEntity);
                await _context.SaveChangesAsync();

                // Xóa mã 2FA đã sử dụng
                await DeleteTwoFactorCode(user.user_id);

                return Ok(new
                {
                    message = "Xác thực 2FA thành công!",
                    success = true,
                    isLogin = true,
                    token = accessToken,
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
                return StatusCode(500, new
                {
                    message = "Lỗi trong quá trình xác thực 2FA",
                    error = ex.Message,
                    success = false
                });
            }
        }

        [HttpPost("public/resend-2fa")]
        public async Task<IActionResult> Resend2FA([FromBody] Resend2FARequest request)
        {
            if (string.IsNullOrEmpty(request.TempToken))
            {
                return Ok(new { message = "Token không hợp lệ", success = false });
            }

            try
            {
                var hashedToken = TokenHasher.HashToken(request.TempToken);
                var tempTokenEntity = await _context.TOKENS
                    .FirstOrDefaultAsync(t =>
                        t.token == hashedToken &&
                        t.type == TokenType.TwoFactor &&
                        t.is_active &&
                        t.expires_at > DateTime.UtcNow);

                if (tempTokenEntity == null)
                {
                    return Ok(new { message = "Phiên xác thực không hợp lệ hoặc đã hết hạn", success = false });
                }

                // Rate limit: ví dụ dùng cache hoặc DB counter
                var cacheKey = $"2fa:resend:{tempTokenEntity.user_id}";
                var resendCountBytes = await _cache.GetAsync(cacheKey);
                int resendCount = 0;
                if (resendCountBytes != null)
                {
                    var resendCountString = Encoding.UTF8.GetString(resendCountBytes);
                    int.TryParse(resendCountString, out resendCount);
                }
                if (resendCount >= 3) // max 3 lần
                {
                    return Ok(new { message = "Quá số lần gửi lại. Vui lòng thử đăng nhập lại.", success = false });
                }

                // Tăng counter và set cooldown (ví dụ 60s)
                resendCount++;
                var newResendCountBytes = Encoding.UTF8.GetBytes(resendCount.ToString());
                await _cache.SetAsync(cacheKey, newResendCountBytes, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });

                // Sinh mã mới
                string newCode = GenerateRandomCode.GenerateTwoFactorCode();

                // Gửi lại
                var user = await _context.USERS.FindAsync(tempTokenEntity.user_id);
                await SendTwoFactorCode(user, newCode);

                // Lưu mã mới (invalidate cũ tự động nếu bạn dùng TTL hoặc overwrite)
                await SaveTwoFactorCode(tempTokenEntity.user_id, newCode);

                return Ok(new
                {
                    message = "Mã xác thực mới đã được gửi",
                    success = true,
                    maskedContact = GetMaskedContact(user),
                    nextResendIn = 60 // frontend dùng để disable button
                });
            }
            catch (Exception ex)
            {
                // log ex
                return StatusCode(500, new { message = "Lỗi hệ thống" + ex.Message, success = false });
            }
        }

        [HttpPost("public/request-login-google")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return BadRequest(new { success = false, message = "Token Google không hợp lệ" });
            }

            GoogleJsonWebSignature.Payload payload;

            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(
                    request.Token,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = new[] { Environment.GetEnvironmentVariable("GOOGLE_APP_CLIENT_ID") ?? _config["Google:ClientId"] }
                    });

            }
            catch
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "Google token không hợp lệ hoặc đã hết hạn"
                });
            }

            if (!payload.EmailVerified)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "Email Google chưa được xác minh"
                });
            }
            if (payload.Issuer != "accounts.google.com" &&
                payload.Issuer != "https://accounts.google.com")
            {
                return Unauthorized("Invalid issuer");
            }
            if (payload.ExpirationTimeSeconds <
                DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return Unauthorized("Google token đã hết hạn");
            }

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.Email == payload.Email);

            if (user == null)
            {
                user = new Users
                {
                    user_id = Guid.NewGuid(),
                    Email = payload.Email,
                    Username = payload.Email.Split('@')[0] + "_" + Guid.NewGuid().ToString("N")[..6],
                    full_name = payload.Name,
                    avatar_url = payload.Picture,
                    Role = "user",
                    password_hash = string.Empty,
                    is_verified = payload.EmailVerified, // Google đã xác minh email
                    created_at = DateTime.UtcNow
                };

                _context.USERS.Add(user);
                await _context.SaveChangesAsync();
            }

            // (Optional) Disable old tokens on same device
            await _context.TOKENS
                .Where(t => t.user_id == user.user_id && t.user_device == request.UserDevice)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.is_active, false));

            var accessToken = _tokenServices.GenerateToken(user.user_id.ToString(), user.Role);
            var hashedToken = TokenHasher.HashToken(accessToken);
            var tokenEntity = new Tokens
            {
                token_id = Guid.NewGuid(),
                user_id = user.user_id,
                token = hashedToken,
                type = TokenType.Access,
                expires_at = DateTime.UtcNow.AddDays(3),
                is_active = true,
                created_at = DateTime.UtcNow,
                user_device = request.UserDevice
            };

            _context.TOKENS.Add(tokenEntity);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đăng nhập Google thành công",
                token = accessToken,
                user = new
                {
                    user.Email,
                    FullName = user.full_name,
                    AvatarUrl = user.avatar_url
                }
            });
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
                    success = true //vẫn đăng xuất nếu token không tồn tại
                });
            }
            await Task.Run(() =>
            {
                tokenEntity.is_active = false;
                _context.TOKENS.Update(tokenEntity);
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
                            Folder = $"DocShare/users/{user.user_id}",
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

        private async Task SendTwoFactorCode(Users user, string code)
        {
            switch (user.two_factor_method)
            {
                case TwoFactorMethod.Email:
                    // Gửi email
                    await _emailService.SendTwoFactorCodeAsync(
                        toEmail: user.Email,
                        recipientName: user.full_name ?? user.Username,
                        twoFactorCode: code
                        );
                    break;
                    //case TwoFactorMethod.SMS:
                    //    // Gửi SMS
                    //    await _smsService.SendTwoFactorCodeSMS(user.PhoneNumber, code);
                    //    break;
                    //case TwoFactorMethod.App:
                    //    // Authenticator App - không cần gửi, user tự generate
                    //    break;
            }
        }
        private async Task SaveTwoFactorCode(Guid userId, string code)
        {
            // Lưu vào Redis hoặc Memory Cache với TTL 10 phút
            var cacheKey = $"2FA:{userId}";
            await _cache.SetStringAsync(cacheKey, code, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });
        }
        private async Task<bool> VerifyTwoFactorCode(Guid userId, string code)
        {
            var cacheKey = $"2FA:{userId}";
            var savedCode = await _cache.GetStringAsync(cacheKey);
            return savedCode == code;
        }
        private async Task DeleteTwoFactorCode(Guid userId)
        {
            var cacheKey = $"2FA:{userId}";
            await _cache.RemoveAsync(cacheKey);
        }
        private string GetMaskedContact(Users user)
        {
            switch (user.two_factor_method)
            {
                case TwoFactorMethod.Email:
                    var email = user.Email;
                    var parts = email.Split('@');
                    return $"{parts[0].Substring(0, 2)}***@{parts[1]}";
                default:
                    return "Authenticator App";
            }
        }
        // ===== REQUEST MODELS =====
        public class Verify2FARequest
        {
            public required string TempToken { get; set; }
            public required string Code { get; set; }
        }
        public class Resend2FARequest
        {
            public required string TempToken { get; set; }
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
}
