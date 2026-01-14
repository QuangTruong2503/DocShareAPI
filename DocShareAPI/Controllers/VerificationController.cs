using DocShareAPI.Data;
using DocShareAPI.EmailServices;
using DocShareAPI.Models;
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

        public VerificationController(DocShareDbContext context, VerifyEmailService verifyEmailService, ResetPasswordEmailService resetPasswordEmailService)
        {
            _context = context;
            _verifyEmailService = verifyEmailService;
            _resetPasswordEmailService = resetPasswordEmailService;
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

            using var tx = await _context.Database.BeginTransactionAsync();

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
            await tx.CommitAsync();

            return Ok(new { message = "Email đã được xác thực thành công." });
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
