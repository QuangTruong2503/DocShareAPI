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
using Newtonsoft.Json;


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
        private static readonly string API_ENDPOINT = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key=AIzaSyCS9_vhAmNF6YGdU5s5fji5VQXiAfA1CAs";

        public VerificationController(DocShareDbContext context, VerifyEmailService verifyEmailService, ResetPasswordEmailService resetPasswordEmailService, HttpClient httpClient)
        {
            _context = context;
            _verifyEmailService = verifyEmailService;
            _resetPasswordEmailService = resetPasswordEmailService;
            _httpClient = httpClient;
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

        [HttpPost("public/upload")]
        public async Task<IActionResult> UploadPdf(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            // Trích xuất văn bản từ PDF
            using var stream = file.OpenReadStream();
            string pdfText = ExtractTextFromPdf(stream);
            string pdfTextRemove = pdfText.Replace("\n", " ").Replace("\r", " ");
            // Lấy danh sách Categories từ database
            var categories = _context.CATEGORIES
                .Select(c => new { c.category_id, c.Name, c.Description })
                .ToList();
            var categoriesJson = JsonConvert.SerializeObject(categories);
            // Gọi Gemini API để phân loại
            var promt = $"Given the following text from a PDF:\n\n{pdfTextRemove}\n\nAnd these categories:\n\n{categoriesJson}\n\nSuggest which category best match the text content. Return only 1 category_id as number.";
            var content = await GenerateContent(promt);
            //string jsonString = JsonConvert.DeserializeObject<string>(content);
            //string contentValue = jsonString.Trim();
            //int categoryId = Int32.Parse(contentValue);
            // Trả về kết quả hoặc lưu vào database
            return Ok(new { content });
        }

        private string ExtractTextFromPdf(Stream stream)
        {
            using var document = new Aspose.Pdf.Document(stream);
            var textAbsorber = new Aspose.Pdf.Text.TextAbsorber();
            document.Pages.Accept(textAbsorber);
            return textAbsorber.Text;
        }
        public static async Task<string> GenerateContent(string prompt)
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    var requestBody = new
                    {
                        contents = new[]
                        {
                        new { parts = new[] { new { text = prompt } } }
                    }
                    };

                    var json = JsonConvert.SerializeObject(requestBody);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await client.PostAsync(API_ENDPOINT, content);
                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();
                    dynamic responseObject = JsonConvert.DeserializeObject(responseBody);

                    return responseObject.candidates[0].content.parts[0].text;
                }
                catch (HttpRequestException ex)
                {
                    // Xử lý lỗi HTTP
                    Console.WriteLine($"Lỗi HTTP: {ex.Message}");
                    return null;
                }
                catch (JsonReaderException ex)
                {
                    // Xử lý lỗi JSON
                    Console.WriteLine($"Lỗi JSON: {ex.Message}");
                    return null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lỗi : {ex.Message}");
                    return null;
                }

            }
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
