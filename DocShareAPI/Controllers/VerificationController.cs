using DocShareAPI.Data;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using PostmarkDotNet;
using DocShareAPI.EmailServices;
using ELearningAPI.Helpers;
using Aspose.Pdf.Text;
using Aspose.Pdf;
using System.Text.Json;
using System.Text;

namespace DocShareAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VerificationController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly VerifyEmailService _verifyEmailService;
        private readonly ResetPasswordEmailService _resetPasswordEmailService;
        private readonly HttpClient _httpClient;

        public VerificationController(DocShareDbContext context, VerifyEmailService verifyEmailService, ResetPasswordEmailService resetPasswordEmailService, HttpClient httpClient)
        {
            _context = context;
            _verifyEmailService = verifyEmailService;
            _resetPasswordEmailService = resetPasswordEmailService;
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
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
        [HttpPost("generate-verify-email-token")]
        public async Task<ActionResult> GenerateVerificationToken([FromBody] string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return BadRequest(new { message = "Email is required." });
            }

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
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
            var tokenRecord = new Tokens
            {
                user_id = user.user_id,
                token = token,
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
                await _verifyEmailService.SendVerificationEmailAsync(
                    toEmail: user.Email,
                    recipientName: user.full_name ?? "Người dùng", // Giả sử có field Name, nếu không thì thay bằng giá trị mặc định
                    verificationToken: token
                );
                return Ok(new 
                { 
                    message = $"Mã xác thực đã được gửi đến email của bạn. Vui lòng kiểm tra 'Thư Mục Rác' nếu không nhận được email ", 
                    token 
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
        [HttpPost("public/verify-email-token")]
        public async Task<ActionResult> VerifyEmailToken([FromBody] string token)
        {
            var tokenRecord = await _context.TOKENS
                .Where(t => t.token == token && t.type == TokenType.EmailVerification && t.is_active && t.expires_at > DateTime.UtcNow)
                .FirstOrDefaultAsync();

            if (tokenRecord == null)
            {
                return BadRequest(new { message = "Token không hợp lệ hoặc đã quá hạn" });
            }

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == tokenRecord.user_id);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            user.is_verified = true;
            tokenRecord.is_active = false;

            _context.USERS.Update(user);
            _context.TOKENS.Update(tokenRecord);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Email đã được xác thực thành công." });
        }

        //Tạo token reset password
        [HttpPost("public/generate-reset-password-token")]
        public async Task<ActionResult> GenerateResetPasswordToken([FromBody] string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return BadRequest(new { message = "Email is required." });
            }
            var user = await _context.USERS.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }
            // Xóa token cũ nếu tồn tại
            var existingToken = await _context.TOKENS
                .Where(t => t.user_id == user.user_id && t.type == TokenType.PasswordReset)
                .FirstOrDefaultAsync();
            if (existingToken != null)
            {
                _context.TOKENS.Remove(existingToken);
                await _context.SaveChangesAsync();
            }
            // Tạo token mới
            var token = GenerateRandomToken();
            var tokenRecord = new Tokens
            {
                user_id = user.user_id,
                token = token,
                type = TokenType.PasswordReset,
                is_active = true,
                created_at = DateTime.UtcNow,
                expires_at = DateTime.UtcNow.AddMinutes(3) // Token valid for 3 minutes
            };
            _context.TOKENS.Add(tokenRecord);
            await _context.SaveChangesAsync();
            // Gửi email reset password
            try
            {
                await _resetPasswordEmailService.SendResetPasswordEmailAsync(
                    toEmail: user.Email,
                    recipientName: user.full_name ?? "Người dùng", // Giả sử có field Name, nếu không thì thay bằng giá trị mặc định
                    resetToken: token
                );
                return Ok(new { message = $"Mã xác thực đã được gửi đến email của bạn. Vui lòng kiểm tra 'Thư Mục Rác' nếu không nhận được email" });
            }
            catch (Exception ex)
            {
                // Có thể rollback token nếu gửi email thất bại
                _context.TOKENS.Remove(tokenRecord);
                await _context.SaveChangesAsync();
                return StatusCode(500, new { message = "Error sending reset password email", error = ex.Message });
            }
        }

        //Xác thực token reset password
        [HttpPost("public/verify-reset-password-token")]
        public async Task<ActionResult> VerifyResetPasswordToken([FromBody] string token)
        {
            var tokenRecord = await _context.TOKENS
                .Where(t => t.token == token && t.type == TokenType.PasswordReset && t.is_active && t.expires_at > DateTime.UtcNow)
                .FirstOrDefaultAsync();
            if (tokenRecord == null)
            {
                return BadRequest(new { message = "Token không hợp lệ hoặc đã quá hạn" });
            }
            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == tokenRecord.user_id);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }
            return Ok(new { message = "Token hợp lệ." });
        }
        // Đổi mật khẩu sau khi xác thực token reset password
        [HttpPost("public/change-password")]
        public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            var tokenRecord = await _context.TOKENS
                .Where(t => t.token == request.token && t.type == TokenType.PasswordReset && t.is_active && t.expires_at > DateTime.UtcNow)
                .FirstOrDefaultAsync();
            if (tokenRecord == null)
            {
                return BadRequest(new { message = "Token không hợp lệ hoặc đã quá hạn" });
            }
            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == tokenRecord.user_id);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }
            user.password_hash = PasswordHasher.HashPassword(request.newPassword);
            tokenRecord.is_active = false;
            _context.USERS.Update(user);
            _context.TOKENS.Update(tokenRecord);
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
