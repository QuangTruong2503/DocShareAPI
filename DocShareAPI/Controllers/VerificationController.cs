using DocShareAPI.Data;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace DocShareAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VerificationController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly TokenServices _tokenServices;
        private readonly ILogger<VerificationController> _logger;

        public VerificationController(DocShareDbContext context, TokenServices tokenServices, ILogger<VerificationController> logger)
        {
            _context = context;
            _tokenServices = tokenServices;
            _logger = logger;
        }

        [HttpPost("public/generate-verify-email-token")]
        public async Task<ActionResult> GenerateVerificationToken([FromBody] string email)
        {
            var user = await _context.USERS.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            if (user.is_verified)
            {
                return BadRequest(new { message = "Người dùng đã xác thực." });
            }

            var existingToken = await _context.TOKENS
                .Where(t => t.user_id == user.user_id && t.type == TokenType.EmailVerification)
                .FirstOrDefaultAsync();

            if (existingToken != null)
            {
                _context.TOKENS.Remove(existingToken);
                await _context.SaveChangesAsync();
            }

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

            // Here you would send the token to the user's email address
            // For example: await _emailService.SendVerificationEmail(user.Email, token);

            return Ok(new {token});
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
