using DocShareAPI.Data;
using DocShareAPI.EmailServices;
using DocShareAPI.Helpers;
using DocShareAPI.Models;
using DocShareAPI.Services;
using DocShareAPI.Services.EmailServices;
using ELearningAPI.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;


namespace DocShareAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VerificationController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly VerifyEmailService _verifyEmailService;
        private readonly ResetPasswordEmailService _resetPasswordEmailService;
        private readonly ITwoFactorEmailService _twoFactorEmailService;
        private readonly INotificationService _notificationService;

        public VerificationController(
            DocShareDbContext context,
            VerifyEmailService verifyEmailService,
            ResetPasswordEmailService resetPasswordEmailService,
            ITwoFactorEmailService twoFactorEmailService,
            INotificationService notificationService)
        {
            _context = context;
            _verifyEmailService = verifyEmailService;
            _resetPasswordEmailService = resetPasswordEmailService;
            _twoFactorEmailService = twoFactorEmailService;
            _notificationService = notificationService;
        }
        //Kiểm tra người dùng đã xác thực
        [HttpGet("check-user-verified")]
        public async Task<IActionResult> CheckUserIsVerify()
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }
            var is_verified = await _context.USERS.Where(u => u.user_id == decodedToken.userID).Select(u => u.is_verified).FirstOrDefaultAsync();
            return Ok(new { is_verified });
        }
        [HttpPost("public/generate-verify-email-token")]
        public async Task<ActionResult> GenerateVerificationToken([FromBody] string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return BadRequest(new { message = "Email is required." });
            }

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                return Ok(new { message = "Nếu email tồn tại, mã xác thực sẽ được gửi." });
            }

            if (user.is_verified)
            {
                return BadRequest(new { message = "Email của bạn được đã xác thực." });
            }

            // Xóa token cũ nếu tồn tại
            var existingToken = await _context.TOKENS
                .Where(t => t.user_id == user.user_id && t.type == TokenType.EmailVerification)
                .FirstOrDefaultAsync();

            if (existingToken != null)
            {
                _context.TOKENS.Remove(existingToken);
                await _context.SaveChangesAsync();
            }

            // Tạo token mới
            var token = GenerateRandomToken();
            var hashedToken = Helpers.TokenHasher.HashToken(token);
            var tokenRecord = new Tokens
            {
                user_id = user.user_id,
                token = hashedToken,
                type = TokenType.EmailVerification,
                is_active = true,
                created_at = DateTime.UtcNow,
                expires_at = DateTime.UtcNow.AddMinutes(3) // Token valid for 3 minutes
            };

            _context.TOKENS.Add(tokenRecord);
            await _context.SaveChangesAsync();

            // Gửi email xác thực
            try
            {
                await _verifyEmailService.SendVerifyEmailAsync(
                    toEmail: user.Email,
                    recipientName: user.full_name ?? "Người dùng", // Giả sử có field Name, nếu không thì thay bằng giá trị mặc định
                    verifyToken: token
                );
                return Ok(new
                {
                    message = $"Mã xác thực đã được gửi đến email của bạn. Vui lòng kiểm tra 'Thư Mục Rác' nếu không nhận được email ",
                });
            }
            catch (Exception ex)
            {
                // Có thể rollback token nếu gửi email thất bại
                _context.TOKENS.Remove(tokenRecord);
                await _context.SaveChangesAsync();
                return StatusCode(500, new { message = "Error sending verification email", error = ex.Message });
            }
        }

        //Xác thực token verify email

        [HttpGet("public/verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return BadRequest(new { message = "Token không hợp lệ." });
            }

            // Lấy tất cả token verify còn hiệu lực
            var tokenRecords = await _context.TOKENS
                .Where(t => t.type == TokenType.EmailVerification
                            && t.is_active
                            && t.expires_at > DateTime.UtcNow)
                .ToListAsync();

            // So sánh hash
            var matchedToken = tokenRecords.FirstOrDefault(t =>
                Helpers.TokenHasher.VerifyToken(token, t.token));

            if (matchedToken == null)
            {
                return BadRequest(new { message = "Token không hợp lệ hoặc đã quá hạn." });
            }
            var user = await _context.USERS
                .FirstOrDefaultAsync(u => u.user_id == matchedToken.user_id);

            if (user == null)
            {
                return BadRequest(new { message = "Token không hợp lệ." });
            }

            if (user.is_verified)
            {
                return Ok(new { message = "Email đã được xác thực trước đó." });
            }

            // Update trạng thái
            user.is_verified = true;

            // Vô hiệu hóa tất cả token verify của user
            var userTokens = await _context.TOKENS
                .Where(t => t.user_id == user.user_id
                            && t.type == TokenType.EmailVerification)
                .ToListAsync();

            foreach (var t in userTokens)
            {
                t.is_active = false;
            }
            await _context.SaveChangesAsync();
            return Ok(new { message = "Email đã được xác thực thành công." });
        }

        [HttpPost("change-email/request-current-verification")]
        public async Task<IActionResult> RequestCurrentEmailVerification()
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized(new { message = "Bạn cần đăng nhập để đổi email.", success = false });
            }

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == decodedToken.userID);
            if (user == null)
            {
                return NotFound(new { message = "Không tìm thấy người dùng.", success = false });
            }

            await DeactivateEmailChangeTokens(user.user_id);

            var code = GenerateRandomCode.GenerateTwoFactorCode();
            var tokenRecord = new Tokens
            {
                token_id = Guid.NewGuid(),
                user_id = user.user_id,
                token = TokenHasher.HashToken(code),
                type = TokenType.EmailChangeCurrentVerification,
                is_active = true,
                created_at = DateTime.UtcNow,
                expires_at = DateTime.UtcNow.AddMinutes(5)
            };

            _context.TOKENS.Add(tokenRecord);
            await _context.SaveChangesAsync();

            try
            {
                await _twoFactorEmailService.SendTwoFactorCodeAsync(
                    toEmail: user.Email,
                    recipientName: user.full_name ?? user.Username,
                    twoFactorCode: code,
                    requestName: "Đổi email");
            }
            catch (Exception ex)
            {
                tokenRecord.is_active = false;
                await _context.SaveChangesAsync();
                return StatusCode(500, new { message = "Không thể gửi mã xác thực đến email hiện tại.", error = ex.Message, success = false });
            }

            return Ok(new
            {
                message = "Mã xác thực đã được gửi đến email hiện tại.",
                success = true,
                currentEmail = MaskEmail(user.Email),
                expiresIn = 300,
                nextApi = new
                {
                    method = "POST",
                    url = "/api/Verification/change-email/verify-current",
                    body = new { code = "123456" }
                }
            });
        }

        [HttpPost("change-email/verify-current")]
        public async Task<IActionResult> VerifyCurrentEmailForChange([FromBody] VerifyCurrentEmailChangeRequest request)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized(new { message = "Bạn cần đăng nhập để đổi email.", success = false });
            }

            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return BadRequest(new { message = "Mã xác thực không hợp lệ.", success = false });
            }

            var hashedCode = TokenHasher.HashToken(request.Code);
            var tokenRecord = await _context.TOKENS
                .FirstOrDefaultAsync(t =>
                    t.user_id == decodedToken.userID &&
                    t.token == hashedCode &&
                    t.type == TokenType.EmailChangeCurrentVerification &&
                    t.is_active &&
                    t.expires_at > DateTime.UtcNow);

            if (tokenRecord == null)
            {
                return BadRequest(new { message = "Mã xác thực không đúng hoặc đã hết hạn.", success = false });
            }

            tokenRecord.is_active = false;
            await DeactivateEmailChangeTokens(decodedToken.userID, TokenType.EmailChangeCurrentVerified);

            var currentEmailVerifiedToken = GenerateRandomToken();
            _context.TOKENS.Add(new Tokens
            {
                token_id = Guid.NewGuid(),
                user_id = decodedToken.userID,
                token = TokenHasher.HashToken(currentEmailVerifiedToken),
                type = TokenType.EmailChangeCurrentVerified,
                is_active = true,
                created_at = DateTime.UtcNow,
                expires_at = DateTime.UtcNow.AddMinutes(10)
            });

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Email hiện tại đã được xác thực. Bạn có thể nhập email mới.",
                success = true,
                currentEmailVerifiedToken,
                expiresIn = 600,
                nextApi = new
                {
                    method = "POST",
                    url = "/api/Verification/change-email/request-new-email-confirmation",
                    body = new
                    {
                        currentEmailVerifiedToken = currentEmailVerifiedToken,
                        newEmail = "new-email@example.com"
                    }
                }
            });
        }

        [HttpPost("change-email/request-new-email-confirmation")]
        public async Task<IActionResult> RequestNewEmailConfirmation([FromBody] RequestNewEmailConfirmationRequest request)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized(new { message = "Bạn cần đăng nhập để đổi email.", success = false });
            }

            var newEmail = NormalizeEmail(request.NewEmail);
            if (string.IsNullOrWhiteSpace(request.CurrentEmailVerifiedToken) || string.IsNullOrWhiteSpace(newEmail) || !IsValidEmail(newEmail))
            {
                return BadRequest(new { message = "Dữ liệu đổi email không hợp lệ.", success = false });
            }

            var verifiedTokenHash = TokenHasher.HashToken(request.CurrentEmailVerifiedToken);
            var verifiedTokenRecord = await _context.TOKENS
                .FirstOrDefaultAsync(t =>
                    t.user_id == decodedToken.userID &&
                    t.token == verifiedTokenHash &&
                    t.type == TokenType.EmailChangeCurrentVerified &&
                    t.is_active &&
                    t.expires_at > DateTime.UtcNow);

            if (verifiedTokenRecord == null)
            {
                return BadRequest(new { message = "Phiên xác thực email hiện tại không hợp lệ hoặc đã hết hạn.", success = false });
            }

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == decodedToken.userID);
            if (user == null)
            {
                return NotFound(new { message = "Không tìm thấy người dùng.", success = false });
            }

            if (string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Email mới phải khác email hiện tại.", success = false });
            }

            var emailExists = await _context.USERS.AnyAsync(u => u.Email == newEmail);
            if (emailExists)
            {
                return BadRequest(new { message = "Email mới đã được sử dụng.", success = false });
            }

            await DeactivateEmailChangeTokens(user.user_id, TokenType.EmailChangeConfirmation);

            var confirmationToken = GenerateRandomToken();
            var confirmationTokenRecord = new Tokens
            {
                token_id = Guid.NewGuid(),
                user_id = user.user_id,
                token = TokenHasher.HashToken(confirmationToken),
                type = TokenType.EmailChangeConfirmation,
                is_active = true,
                user_device = newEmail,
                created_at = DateTime.UtcNow,
                expires_at = DateTime.UtcNow.AddMinutes(10)
            };

            _context.TOKENS.Add(confirmationTokenRecord);
            await _context.SaveChangesAsync();

            try
            {
                await _verifyEmailService.SendChangeEmailConfirmationAsync(
                    toEmail: newEmail,
                    recipientName: user.full_name ?? user.Username,
                    verifyToken: confirmationToken);
            }
            catch (Exception ex)
            {
                confirmationTokenRecord.is_active = false;
                await _context.SaveChangesAsync();
                return StatusCode(500, new { message = "Không thể gửi email xác nhận đến email mới.", error = ex.Message, success = false });
            }

            return Ok(new
            {
                message = "Email xác nhận đã được gửi đến email mới. Vui lòng kiểm tra và kích hoạt.",
                success = true,
                pendingEmail = MaskEmail(newEmail),
                expiresIn = 600,
                confirmApi = new
                {
                    method = "GET",
                    url = "/api/Verification/public/confirm-change-email?token={token_from_email}"
                }
            });
        }

        [HttpGet("public/confirm-change-email")]
        public async Task<IActionResult> ConfirmChangeEmail([FromQuery] string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return BadRequest(new { message = "Token không hợp lệ.", success = false });
            }

            var hashedToken = TokenHasher.HashToken(token);
            var tokenRecord = await _context.TOKENS
                .FirstOrDefaultAsync(t =>
                    t.token == hashedToken &&
                    t.type == TokenType.EmailChangeConfirmation &&
                    t.is_active &&
                    t.expires_at > DateTime.UtcNow);

            if (tokenRecord == null || string.IsNullOrWhiteSpace(tokenRecord.user_device))
            {
                return BadRequest(new { message = "Token đổi email không hợp lệ hoặc đã hết hạn.", success = false });
            }

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == tokenRecord.user_id);
            if (user == null)
            {
                return NotFound(new { message = "Không tìm thấy người dùng.", success = false });
            }

            var newEmail = NormalizeEmail(tokenRecord.user_device);
            var emailExists = await _context.USERS.AnyAsync(u => u.Email == newEmail && u.user_id != user.user_id);
            if (emailExists)
            {
                tokenRecord.is_active = false;
                await _context.SaveChangesAsync();
                return BadRequest(new { message = "Email mới đã được sử dụng bởi tài khoản khác.", success = false });
            }

            var oldEmail = user.Email;
            user.Email = newEmail;
            user.is_verified = true;

            await DeactivateEmailChangeTokens(user.user_id);
            await _context.SaveChangesAsync();

            await _notificationService.CreateAsync(
                recipientUserId: user.user_id,
                type: "EMAIL_CHANGED",
                title: "Email tài khoản đã được thay đổi",
                message: $"Email đăng nhập của bạn đã được đổi sang {MaskEmail(user.Email)}.",
                targetUrl: "/profile",
                metadata: new { old_email = MaskEmail(oldEmail), new_email = MaskEmail(user.Email) });

            return Ok(new
            {
                message = "Đổi email thành công.",
                success = true,
                oldEmail = MaskEmail(oldEmail),
                email = user.Email
            });
        }


        //Tạo token reset password
        [HttpPost("public/generate-reset-password-token")]
        public async Task<ActionResult> GenerateResetPasswordToken([FromBody] string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest();

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.Email == email);

            // Không tiết lộ user tồn tại hay không
            if (user == null)
            {
                return Ok(new { message = "Nếu email tồn tại, hướng dẫn đặt lại mật khẩu sẽ được gửi." });
            }

            var rawToken = GenerateRandomToken();
            var hashedToken = Helpers.TokenHasher.HashToken(rawToken);

            var existingToken = await _context.TOKENS
                .FirstOrDefaultAsync(t =>
                    t.user_id == user.user_id &&
                    t.type == TokenType.PasswordReset);

            if (existingToken != null)
                _context.TOKENS.Remove(existingToken);

            var tokenRecord = new Tokens
            {
                user_id = user.user_id,
                token = hashedToken,
                type = TokenType.PasswordReset,
                is_active = true,
                created_at = DateTime.UtcNow,
                expires_at = DateTime.UtcNow.AddMinutes(3)
            };

            _context.TOKENS.Add(tokenRecord);
            await _context.SaveChangesAsync(); // EF tự tạo transaction

            // Gửi email sau khi lưu thành công
            await _resetPasswordEmailService.SendResetPasswordEmailAsync(
                user.Email,
                user.full_name ?? "Người dùng",
                rawToken
            );

            return Ok(new
            {
                message = "Nếu email tồn tại, hướng dẫn đặt lại mật khẩu sẽ được gửi."
            });
        }


        //Xác thực token reset password
        [HttpPost("public/verify-reset-password-token")]
        public async Task<ActionResult> VerifyResetPasswordToken([FromBody] string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest(new { message = "Token không hợp lệ." });

            var hashedToken = Helpers.TokenHasher.HashToken(token);

            var tokenRecord = await _context.TOKENS
                .Where(t =>
                    t.token == hashedToken &&
                    t.type == TokenType.PasswordReset &&
                    t.is_active &&
                    t.expires_at > DateTime.UtcNow)
                .FirstOrDefaultAsync();

            if (tokenRecord == null)
            {
                return BadRequest(new { message = "Token không hợp lệ hoặc đã quá hạn." });
            }

            return Ok(new { message = "Token hợp lệ." });
        }

        [HttpPost("public/change-password")]
        public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.token) ||
                string.IsNullOrWhiteSpace(request.newPassword))
            {
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });
            }

            var hashedToken = Helpers.TokenHasher.HashToken(request.token);

            var tokenRecord = await _context.TOKENS
                .Where(t =>
                    t.token == hashedToken &&
                    t.type == TokenType.PasswordReset &&
                    t.is_active &&
                    t.expires_at > DateTime.UtcNow)
                .FirstOrDefaultAsync();

            if (tokenRecord == null)
            {
                return BadRequest(new { message = "Token không hợp lệ hoặc đã quá hạn." });
            }

            var user = await _context.USERS
                .FirstOrDefaultAsync(u => u.user_id == tokenRecord.user_id);

            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            user.password_hash = PasswordHasher.HashPassword(request.newPassword);

            // Vô hiệu hóa token sau khi dùng
            tokenRecord.is_active = false;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Đổi mật khẩu thành công." });
        }


        //Reset password request model
        public class ResetPasswordRequest
        {
            public required string token { get; set; }
            public required string newPassword { get; set; }
        }

        public class VerifyCurrentEmailChangeRequest
        {
            public required string Code { get; set; }
        }

        public class RequestNewEmailConfirmationRequest
        {
            public required string CurrentEmailVerifiedToken { get; set; }
            public required string NewEmail { get; set; }
        }

        private async Task DeactivateEmailChangeTokens(Guid userId, params TokenType[] tokenTypes)
        {
            var types = tokenTypes.Length > 0
                ? tokenTypes
                : new[]
                {
                    TokenType.EmailChangeCurrentVerification,
                    TokenType.EmailChangeCurrentVerified,
                    TokenType.EmailChangeConfirmation
                };

            var tokens = await _context.TOKENS
                .Where(t => t.user_id == userId && types.Contains(t.type) && t.is_active)
                .ToListAsync();

            foreach (var token in tokens)
            {
                token.is_active = false;
            }
        }

        private static string NormalizeEmail(string? email)
        {
            return email?.Trim().ToLowerInvariant() ?? string.Empty;
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                var address = new System.Net.Mail.MailAddress(email);
                return address.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private static string MaskEmail(string email)
        {
            var parts = email.Split('@');
            if (parts.Length != 2)
            {
                return email;
            }

            var name = parts[0];
            var domain = parts[1];
            var visibleName = name.Length <= 2 ? name[..1] : name[..2];

            return $"{visibleName}***@{domain}";
        }

        private string GenerateRandomToken()
        {
            var tokenData = new byte[128];
            RandomNumberGenerator.Fill(tokenData);
            return Convert.ToBase64String(tokenData)
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "");
        }

    }
}
