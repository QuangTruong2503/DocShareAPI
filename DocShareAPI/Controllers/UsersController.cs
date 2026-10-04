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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.Data;
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
        //Get My Profile
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
                .Select(u => new { u.user_id, u.Username, u.full_name, u.Email, u.avatar_url, u.created_at, u.Role, u.is_verified, u.two_factor_enabled })
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
        //Login with 2FA Check
        [HttpPost("public/request-login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest loginRequest)
        {
            if (string.IsNullOrEmpty(loginRequest.Email) || string.IsNullOrEmpty(loginRequest.Password))
            {
                return Ok(new
                {
                    message = "Email và mật khẩu không được để trống.",
                    success = false,
                    isLogin = false
                });
            }

            try
            {
                var user = await _context.USERS
                    .FirstOrDefaultAsync(u => u.Email == loginRequest.Email.Trim().ToLower() || u.Username == loginRequest.Email.Trim());

                if (user == null)
                {
                    return Ok(new
                    {
                        message = "Tài khoản hoặc email không tồn tại.",
                        success = false,
                        isLogin = false
                    });
                }

                if (!PasswordHasher.VerifyPassword(loginRequest.Password, user.password_hash))
                {
                    return Ok(new
                    {
                        message = "Mật khẩu không chính xác.",
                        success = false,
                        isLogin = false
                    });
                }

                // ===== KIỂM TRA 2FA =====
                if (user.two_factor_enabled)
                {
                    // Tạo temporary token để verify 2FA
                    var tempToken = _tokenServices.GenerateToken(user.user_id.ToString(), user.Role, "TwoFactorLogin");
                    var hashedTempToken = TokenHasher.HashToken(tempToken);

                    // Lưu temp token vào database
                    var tempTokenEntity = new Tokens
                    {
                        token_id = Guid.NewGuid(),
                        user_id = user.user_id,
                        token = hashedTempToken,
                        type = TokenType.TwoFactorLogin, // Cần thêm enum value này
                        expires_at = DateTime.UtcNow.AddMinutes(5), // Token 2FA chỉ tồn tại 5 phút
                        is_active = true,
                        created_at = DateTime.UtcNow,
                    };

                    _context.TOKENS.Add(tempTokenEntity);
                    await _context.SaveChangesAsync();

                    // Gửi mã 2FA (tùy vào phương thức)
                    string twoFactorCode = GenerateRandomCode.GenerateTwoFactorCode(); // Tạo mã 6 số
                    if (!await TwoFactorChallenges.SaveAsync(_context, tempTokenEntity.token_id, twoFactorCode))
                        return StatusCode(500, new { message = "Không thể lưu mã xác thực. Vui lòng thử lại.", success = false });
                    await SendTwoFactorCode(user, twoFactorCode, "Đăng nhập");

                    return Ok(new
                    {
                        message = "Yêu cầu xác thực 2FA",
                        success = true,
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
                var tokenExpiresAt = DateTime.UtcNow.Add(TokenServices.AccessTokenLifetime);

                var tokenEntity = new Tokens
                {
                    token_id = Guid.NewGuid(),
                    user_id = user.user_id,
                    token = hashedToken,
                    type = TokenType.Access,
                    expires_at = tokenExpiresAt,
                    is_active = true,
                    created_at = DateTime.UtcNow,
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
                        message = "Lỗi khi lưu token vào cơ sở dữ liệu",
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
                    expiresAt = tokenExpiresAt,
                    expiresIn = (int)TokenServices.AccessTokenLifetime.TotalSeconds,
                    user = BuildUserResponse(user)
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
        [HttpPost("public/verify-2fa-login")]
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
                        t.type == TokenType.TwoFactorLogin &&
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

                // Verify a persisted, single-use challenge with a shared attempt budget.
                var isValidCode = await TwoFactorChallenges.VerifyAsync(_context, tempTokenEntity.token_id, request.Code);
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
                if (user == null)
                {
                    return Ok(new
                    {
                        message = "Phiên xác thực không hợp lệ",
                        success = false
                    });
                }

                var accessToken = _tokenServices.GenerateToken(user.user_id.ToString(), user.Role);
                var hashedAccessToken = TokenHasher.HashToken(accessToken);
                var tokenExpiresAt = DateTime.UtcNow.Add(TokenServices.AccessTokenLifetime);

                var accessTokenEntity = new Tokens
                {
                    token_id = Guid.NewGuid(),
                    user_id = user.user_id,
                    token = hashedAccessToken,
                    type = TokenType.Access,
                    expires_at = tokenExpiresAt,
                    is_active = true,
                    created_at = DateTime.UtcNow
                };

                _context.TOKENS.Add(accessTokenEntity);
                var googleSubject = TwoFactorChallenges.PendingGoogleSubject(tempTokenEntity.user_device);
                if (googleSubject != null)
                {
                    await using var identityMutex = await DatabaseMutex.Acquire(_context, "docshare:google-identities");
                    var existingIdentity = await _context.EXTERNAL_IDENTITIES.FirstOrDefaultAsync(i => i.provider == "google" && (i.subject == googleSubject || i.user_id == user.user_id));
                    if (existingIdentity != null && (existingIdentity.subject != googleSubject || existingIdentity.user_id != user.user_id)) return Conflict(new { message = "Định danh Google đã được liên kết với tài khoản khác." });
                    if (existingIdentity == null) _context.EXTERNAL_IDENTITIES.Add(new ExternalIdentity {provider="google",subject=googleSubject,user_id=user.user_id});
                    await _context.SaveChangesAsync();
                }
                else await _context.SaveChangesAsync();

                // Xóa mã 2FA đã sử dụng
                await DeleteTwoFactorCode(user.user_id);

                return Ok(new
                {
                    message = "Xác thực 2FA thành công!",
                    success = true,
                    isLogin = true,
                    token = accessToken,
                    expiresAt = tokenExpiresAt,
                    expiresIn = (int)TokenServices.AccessTokenLifetime.TotalSeconds,
                    user = BuildUserResponse(user)
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

        // ===== ENDPOINT GỬI LẠI MÃ 2FA =====
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
                        t.type == TokenType.TwoFactorLogin &&
                        t.is_active &&
                        t.expires_at > DateTime.UtcNow);

                if (tempTokenEntity == null)
                {
                    return Ok(new { message = "Phiên xác thực không hợp lệ hoặc đã hết hạn", success = false });
                }

                // Resend limits and cooldown are persisted with the challenge.
                // Sinh mã mới
                string newCode = GenerateRandomCode.GenerateTwoFactorCode();

                // Gửi lại
                var user = await _context.USERS.FindAsync(tempTokenEntity.user_id);
                if (user == null)
                {
                    return Ok(new { message = "Không tìm thấy người dùng", success = false });
                }

                if (!await TwoFactorChallenges.SaveAsync(_context, tempTokenEntity.token_id, newCode, resend: true))
                    return StatusCode(429, new { success = false, message = "Vui lòng chờ 60 giây. Mỗi phiên được gửi lại tối đa 3 lần." });
                await SendTwoFactorCode(user, newCode, "Đăng nhập");

                // Lưu mã mới (invalidate cũ tự động nếu bạn dùng TTL hoặc overwrite)


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

        // ===== ENDPOINTS BẬT 2FA =====
        // Bật 2FA - Bước 1: Yêu cầu bật và gửi mã xác thực
        [HttpPost("request-enable-2fa")]
        [Authorize]  // Chỉ user đã đăng nhập mới được bật 2FA
        public async Task<IActionResult> RequestEnableTwoFactor()
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }

            var user = await _context.USERS
                .FirstOrDefaultAsync(u => u.user_id == decodedToken.userID);

            if (user == null)
            {
                return NotFound(new { message = "Không tìm thấy người dùng", success = false });
            }

            if (user.two_factor_enabled)
            {
                return Ok(new
                {
                    message = "Xác thực hai yếu tố đã được bật từ trước",
                    success = false,
                    alreadyEnabled = true
                });
            }

            // Tạo temporary token (dùng để xác minh khi verify code)
            var tempToken = _tokenServices.GenerateToken(user.user_id.ToString(), user.Role, "TwoFactorEnable");
            var hashedTempToken = TokenHasher.HashToken(tempToken);

            var tempTokenEntity = new Tokens
            {
                token_id = Guid.NewGuid(),
                user_id = user.user_id,
                token = hashedTempToken,
                type = TokenType.TwoFactorEnable,  // Nên có enum riêng cho setup để phân biệt với login
                expires_at = DateTime.UtcNow.AddMinutes(10), // 10 phút cho setup
                is_active = true,
                created_at = DateTime.UtcNow
            };

            _context.TOKENS.Add(tempTokenEntity);
            await _context.SaveChangesAsync();

            // Tạo và gửi mã 2FA
            string twoFactorCode = GenerateRandomCode.GenerateTwoFactorCode();
            if (!await TwoFactorChallenges.SaveAsync(_context, tempTokenEntity.token_id, twoFactorCode))
                return StatusCode(500, new { message = "Không thể lưu mã xác thực. Vui lòng thử lại.", success = false });
            await SendTwoFactorCode(user, twoFactorCode, "Bật xác thực hai yếu tố");

            return Ok(new
            {
                success = true,
                message = "Mã xác thực đã được gửi đến " + GetMaskedContact(user),
                require2FA = true,
                tempToken = tempToken,           // gửi về client để dùng khi verify
                twoFactorMethod = user.two_factor_method.ToString(),
                maskedContact = GetMaskedContact(user),
                expiresIn = 600 // 10 phút (giây)
            });
        }        //Verify 2FA Enable
                 // Bật 2FA - Bước 2: Xác minh mã để hoàn tất bật 2FA
        [HttpPost("request-verify-2fa-setup")]
        [Authorize]
        public async Task<IActionResult> VerifyTwoFactorSetup([FromBody] Verify2FASetupRequest request)
        {
            if (string.IsNullOrEmpty(request.TempToken) || string.IsNullOrEmpty(request.Code))
            {
                return BadRequest(new { message = "Thiếu thông tin xác thực", success = false });
            }
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }
            try
            {
                // Kiểm tra temp token
                var hashedTempToken = TokenHasher.HashToken(request.TempToken);
                var tempTokenEntity = await _context.TOKENS
                    .Include(t => t.Users)
                    .FirstOrDefaultAsync(t =>
                        t.token == hashedTempToken &&
                        t.type == TokenType.TwoFactorEnable &&
                        t.is_active &&
                        t.expires_at > DateTime.UtcNow);

                if (tempTokenEntity == null || tempTokenEntity.Users == null)
                {
                    return Ok(new
                    {
                        message = "Phiên thiết lập 2FA không hợp lệ hoặc đã hết hạn",
                        success = false
                    });
                }

                var user = tempTokenEntity.Users;

                // Kiểm tra mã 2FA
                var isValidCode = await TwoFactorChallenges.VerifyAsync(_context, tempTokenEntity.token_id, request.Code);
                if (!isValidCode)
                {
                    return Ok(new
                    {
                        message = "Mã xác thực không đúng",
                        success = false
                    });
                }

                // === Bật 2FA thành công ===
                user.two_factor_enabled = true;
                user.two_factor_method = TwoFactorMethod.Email;
                user.two_factor_verified_at = DateTime.UtcNow;

                // Vô hiệu hóa temp token
                tempTokenEntity.is_active = false;

                await _context.SaveChangesAsync();

                // Xóa mã 2FA đã dùng
                await DeleteTwoFactorCode(user.user_id);

                return Ok(new
                {
                    success = true,
                    message = "Xác thực hai yếu tố đã được bật thành công!",
                    twoFactorEnabled = true,
                    twoFactorMethod = user.two_factor_method.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi verify setup 2FA cho user {UserId}", decodedToken?.userID);
                return StatusCode(500, new
                {
                    message = "Lỗi hệ thống khi thiết lập 2FA",
                    success = false
                });
            }
        }

        [HttpPost("request-disable-2fa")]
        [Authorize]
        public async Task<IActionResult> DisableTwoFactor([FromBody] Disable2FARequest? request = null)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == decodedToken.userID);
            if (user == null)
            {
                return NotFound(new { message = "Không tìm thấy người dùng", success = false });
            }

            if (!user.two_factor_enabled)
            {
                return Ok(new
                {
                    success = true,
                    message = "Xác thực hai yếu tố đã được tắt từ trước.",
                    twoFactorEnabled = false
                });
            }

            if (!string.IsNullOrWhiteSpace(request?.Password) &&
                !string.IsNullOrWhiteSpace(user.password_hash) &&
                !PasswordHasher.VerifyPassword(request.Password, user.password_hash))
            {
                return BadRequest(new { success = false, message = "Mật khẩu không chính xác." });
            }

            user.two_factor_enabled = false;
            user.two_factor_method = TwoFactorMethod.Email;
            user.two_factor_verified_at = DateTime.UtcNow;

            await _context.TOKENS
                .Where(t => t.user_id == user.user_id &&
                    (t.type == TokenType.TwoFactorEnable || t.type == TokenType.TwoFactorLogin) &&
                    t.is_active)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.is_active, false));

            await DeleteTwoFactorCode(user.user_id);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã tắt xác thực hai yếu tố.",
                twoFactorEnabled = false,
                twoFactorMethod = user.two_factor_method.ToString()
            });
        }

        //Login with Google
        [HttpPost("link-google")]
        public async Task<IActionResult> LinkGoogle([FromBody] GoogleLoginRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse) return Unauthorized();
            return await GoogleLogin(request);
        }
        [HttpPost("public/request-login-google")]
        [HttpPost("~/Users/public/request-login-google")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.token))
            {
                return BadRequest(new { success = false, message = "Token Google không hợp lệ" });
            }

            var googleClientId = Environment.GetEnvironmentVariable("GOOGLE_APP_CLIENT_ID") ?? _config["Google:ClientId"];
            if (string.IsNullOrWhiteSpace(googleClientId))
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Google Client ID chưa được cấu hình."
                });
            }

            GoogleJsonWebSignature.Payload payload;

            try
            {
                payload = await ValidateGoogleTokenAsync(request.token, googleClientId);

            }
            catch
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "Google token không hợp lệ hoặc đã hết hạn"
                });
            }

            if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email))
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
                return Unauthorized(new
                {
                    success = false,
                    message = "Google token không đúng nhà phát hành."
                });
            }
            if (payload.ExpirationTimeSeconds <
                DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "Google token đã hết hạn"
                });
            }

            if (string.IsNullOrWhiteSpace(payload.Subject)) return BadRequest(new { message = "Google token không có định danh tài khoản." });
            await using var identityLock = await DatabaseMutex.Acquire(_context, "docshare:google-identities");
            var identity = await _context.EXTERNAL_IDENTITIES.Include(i => i.User).FirstOrDefaultAsync(i => i.provider == "google" && i.subject == payload.Subject);
            var user = identity?.User;
            if (identity != null && HttpContext.Request.Path.Value?.EndsWith("/link-google", StringComparison.OrdinalIgnoreCase) == true && HttpContext.Items["DecodedToken"] is DecodedTokenResponse linkingUser && linkingUser.userID != identity.user_id)
                return Conflict(new { message = "Định danh Google đã liên kết với tài khoản khác." });
            if (identity == null)
            {
                var normalizedEmail = payload.Email.Trim().ToLowerInvariant();
                user = await _context.USERS.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
                var confirmedSession = HttpContext.Items["DecodedToken"] is DecodedTokenResponse session && user != null && session.userID == user.user_id;
                if (HttpContext.Request.Path.Value?.EndsWith("/link-google", StringComparison.OrdinalIgnoreCase) == true && !confirmedSession)
                    return Conflict(new { message = "Định danh Google phải có cùng email với tài khoản đang đăng nhập." });
                if (user != null && await _context.EXTERNAL_IDENTITIES.AnyAsync(i => i.user_id == user.user_id && i.provider == "google"))
                    return Conflict(new { message = "Tài khoản đã liên kết với một định danh Google khác." });
            }

            if (user == null)
            {
                user = new Users
                {
                    user_id = Guid.NewGuid(),
                    Email = payload.Email.Trim().ToLowerInvariant(),
                    Username = payload.Email.Split('@')[0] + "_" + Guid.NewGuid().ToString("N")[..6],
                    full_name = payload.Name,
                    avatar_url = payload.Picture,
                    Role = "user",
                    password_hash = string.Empty,
                    is_verified = payload.EmailVerified, // Google đã xác minh email
                    created_at = DateTime.UtcNow
                };

                _context.USERS.Add(user);
            }

            // Google has verified ownership of the email before reaching this point.
            // Reuse the existing account without requiring its local password.
            user.is_verified = true;

            if (identity == null && !user.two_factor_enabled)
            {
                _context.EXTERNAL_IDENTITIES.Add(new ExternalIdentity { provider = "google", subject = payload.Subject, user_id = user.user_id });
                await _context.SaveChangesAsync();
            }

            // ===== KIỂM TRA 2FA =====
                if (user.two_factor_enabled)
                {
                    // Tạo temporary token để verify 2FA
                    var tempToken = _tokenServices.GenerateToken(user.user_id.ToString(), user.Role, "TwoFactorLogin");
                    var hashedTempToken = TokenHasher.HashToken(tempToken);

                    // Lưu temp token vào database
                    var tempTokenEntity = new Tokens
                    {
                        token_id = Guid.NewGuid(),
                        user_id = user.user_id,
                        token = hashedTempToken,
                        type = TokenType.TwoFactorLogin, // Cần thêm enum value này
                        expires_at = DateTime.UtcNow.AddMinutes(5), // Token 2FA chỉ tồn tại 5 phút
                        is_active = true,
                        created_at = DateTime.UtcNow,
                    };

                    _context.TOKENS.Add(tempTokenEntity);
                    await _context.SaveChangesAsync();

                    // Gửi mã 2FA (tùy vào phương thức)
                    string twoFactorCode = GenerateRandomCode.GenerateTwoFactorCode(); // Tạo mã 6 số
                    if (!await TwoFactorChallenges.SaveAsync(_context, tempTokenEntity.token_id, twoFactorCode, googleSubject: identity == null ? payload.Subject : null))
                        return StatusCode(500, new { message = "Không thể lưu mã xác thực. Vui lòng thử lại.", success = false });
                    await SendTwoFactorCode(user, twoFactorCode, "Đăng nhập");

                    return Ok(new
                    {
                        message = "Yêu cầu xác thực 2FA",
                        success = true,
                        isLogin = false,
                        require2FA = true,
                        twoFactorMethod = user.two_factor_method.ToString(),
                        tempToken = tempToken, // Token tạm để verify 2FA
                        maskedContact = GetMaskedContact(user) // Ẩn một phần email/số điện thoại
                    });
                }


            // Disable old tokens on same device
            await _context.TOKENS
                .Where(t => t.user_id == user.user_id && t.user_device == request.userDevice)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.is_active, false));

            var accessToken = _tokenServices.GenerateToken(user.user_id.ToString(), user.Role);
            var hashedToken = TokenHasher.HashToken(accessToken);
            var tokenExpiresAt = DateTime.UtcNow.Add(TokenServices.AccessTokenLifetime);
            var tokenEntity = new Tokens
            {
                token_id = Guid.NewGuid(),
                user_id = user.user_id,
                token = hashedToken,
                type = TokenType.Access,
                expires_at = tokenExpiresAt,
                is_active = true,
                created_at = DateTime.UtcNow,
                user_device = request.userDevice
            };

            _context.TOKENS.Add(tokenEntity);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đăng nhập Google thành công",
                isLogin = true,
                token = accessToken,
                expiresAt = tokenExpiresAt,
                expiresIn = (int)TokenServices.AccessTokenLifetime.TotalSeconds,
                user = BuildUserResponse(user)
            });
        }

        protected virtual Task<GoogleJsonWebSignature.Payload> ValidateGoogleTokenAsync(string token, string clientId)
            => GoogleJsonWebSignature.ValidateAsync(token, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId }
            });

        //Logout
        [HttpPost("request-logout")]
        public async Task<IActionResult> Logout([FromBody] string token)
        {
            //Kiểm tra token
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return BadRequest(new
                {
                    message = "Token không hợp lệ",
                    success = false
                });
            }

            var hashedToken = TokenHasher.HashToken(token);
            var tokenEntity = await _context.TOKENS.FirstOrDefaultAsync(t =>
                t.token == hashedToken &&
                t.user_id == decodedToken.userID &&
                t.type == TokenType.Access &&
                t.is_active);

            if (tokenEntity == null)
            {
                return Ok(new
                {
                    message = "Token không tồn tại",
                    success = true //vẫn đăng xuất nếu token không tồn tại
                });
            }

            tokenEntity.is_active = false;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Đăng xuất thành công",
                success = true
            });
        }

        //Register / Đăng ký thành viên
        [HttpPost("public/request-register")]
        public async Task<IActionResult> Register([FromBody] LoginRequestDTO request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new
                {
                    message = "Email và mật khẩu không được để trống",
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
                    Username = request.Email.Split('@')[0] + "_" + Guid.NewGuid().ToString("N")[..8],
                    Email = request.Email.Trim().ToLowerInvariant(),
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

        //Cập nhật hình ảnh đại diện
        [HttpPut("update-avatar")]
        public async Task<IActionResult> UpdateImage(IFormFile image)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }

            if (image == null || image.Length == 0)
                return BadRequest("Không có ảnh được tải lên");

            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/jpg" };
            if (!allowedTypes.Contains(image.ContentType))
                return BadRequest("Định dạng ảnh không hợp lệ");

            if (image.Length > 3 * 1024 * 1024)
                return BadRequest("Ảnh không được vượt quá 3MB");

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == decodedToken.userID);
            if (user == null)
                return BadRequest("Không tìm thấy người dùng");

            using var stream = image.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(image.FileName, stream),
                Folder = $"DocShare/users/{user.user_id}",
                Transformation = new Transformation()
                    .Width(250)
                    .Height(250)
                    .Crop("auto")
                    .Gravity("auto")
                    .AspectRatio(1.0)
            };

            var uploadResult = await _cloudinaryService.Cloudinary.UploadAsync(uploadParams);

            if (uploadResult.Error != null)
                return StatusCode(500, $"Không thể tải ảnh đại diện lên: {uploadResult.Error.Message}");

            if (!string.IsNullOrEmpty(user.avatar_public_id))
            {
                await _cloudinaryService.Cloudinary.DeleteResourcesAsync(
                    new DelResParams
                    {
                        PublicIds = new List<string> { user.avatar_public_id },
                        ResourceType = ResourceType.Image
                    });
            }

            user.avatar_url = uploadResult.SecureUrl.ToString();
            user.avatar_public_id = uploadResult.PublicId;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Cập nhật ảnh đại diện thành công",
                user = BuildUserResponse(user)
            });
        }

        //Cập nhật thông tin cá nhân
        [HttpPut("update-profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UserUpdateDTO dto)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }

            // LẤY USER TỪ TOKEN - không tin user_id từ client
            var user = await _context.USERS
                .FirstOrDefaultAsync(u => u.user_id == decodedToken.userID);

            if (user == null)
            {
                return NotFound(new { message = "Không tìm thấy người dùng", success = false });
            }

            // Validation cơ bản (nên dùng FluentValidation để đẹp hơn)
            if (string.IsNullOrWhiteSpace(dto.fullname))
                return BadRequest(new { message = "Họ tên không được để trống", success = false });

            // Cập nhật
            user.full_name = dto.fullname.Trim();

            _context.USERS.Update(user);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Cập nhật thông tin thành công",
                user = BuildUserResponse(user)
            });
        }

        // ===== PRIVATE METHODS =====
        // Kiểm tra định dạng email
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private static object BuildUserResponse(Users user)
        {
            return new
            {
                userId = user.user_id,
                username = user.Username,
                email = user.Email,
                fullName = user.full_name,
                avatarUrl = user.avatar_url,
                role = user.Role,
                isVerified = user.is_verified,
                twoFactorEnabled = user.two_factor_enabled
            };
        }

        // Gửi mã 2FA
        private async Task SendTwoFactorCode(Users user, string code, string requestName)
        {
            switch (user.two_factor_method)
            {
                case TwoFactorMethod.Email:
                    // Gửi email
                    await _emailService.SendTwoFactorCodeAsync(
                        toEmail: user.Email,
                        recipientName: user.full_name ?? user.Username,
                        twoFactorCode: code,
                        requestName: requestName
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
        // Xóa mã 2FA sau khi sử dụng
        private async Task DeleteTwoFactorCode(Guid userId)
        {
            var cacheKey = $"2FA:{userId}";
            await _cache.RemoveAsync(cacheKey);
        }
        // Ẩn một phần thông tin liên lạc khi gửi về client
        private string GetMaskedContact(Users user)
        {
            switch (user.two_factor_method)
            {
                case TwoFactorMethod.Email:
                    var email = user.Email;
                    var parts = email.Split('@');
                    return $"{parts[0][..Math.Min(3, parts[0].Length)]}***@{parts[1]}";
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
        public class Verify2FASetupRequest
        {
            public required string TempToken { get; set; }
            public required string Code { get; set; }
        }

        public class Disable2FARequest
        {
            public string? Password { get; set; }
        }
    }
}
