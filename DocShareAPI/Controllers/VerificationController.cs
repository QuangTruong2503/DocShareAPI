using DocShareAPI.Data;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using PostmarkDotNet;
using DocShareAPI.EmailServices;

namespace DocShareAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VerificationController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly VerifyEmailService _verifyEmailService;

        public VerificationController(DocShareDbContext context, VerifyEmailService verifyEmailService)
        {
            _context = context;
            _verifyEmailService = verifyEmailService;
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
