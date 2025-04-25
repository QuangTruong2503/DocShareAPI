using DocShareAPI.Data;
using DocShareAPI.DataTransferObject;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.Crmf;
using System.Text;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DocShareAPI.Controllers.Public
{
    [Route("api/public")]
    [ApiController]
    public class GeminiAIGenerateController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly string _apiKey;

        public GeminiAIGenerateController(
            DocShareDbContext context,
            ICloudinaryService cloudinaryService,
            IOptions<GeminiAIOptions> geminiOptions)
        {
            _context = context;
            _cloudinaryService = cloudinaryService;
            _apiKey = geminiOptions.Value.ApiKey ?? throw new ArgumentNullException("Gemini API Key is missing.");
        }
        [HttpGet("gemini/document-summary")]
        public async Task<IActionResult> SummarizeDocument([FromQuery] int documentId)
        {
            // 1. Truy vấn document từ DB
            var document = await _context.DOCUMENTS.FindAsync(documentId);
            if (document == null)
                return NotFound("Document not found.");

            if (string.IsNullOrEmpty(document.file_url))
                return BadRequest("Document URL is missing.");

            try
            {
                // 2. Tải file PDF từ Cloudinary
                using var httpClient = new HttpClient();
                var pdfStream = await httpClient.GetStreamAsync(document.file_url);

                // 3. Trích xuất văn bản
                string pdfText = ExtractTextFromPdf(pdfStream);
                string cleanedText = pdfText.Replace("\n", " ").Replace("\r", " ");

                // 4. Gửi prompt tới Gemini
                var prompt = $"Hãy đọc và tóm tắt nội dung chính của tài liệu PDF sau:\n\n{cleanedText}\n\nTóm tắt ngắn gọn, rõ ràng(có phân cấp đoạn văn bản giúp chuyên nghiệp hơn).";
                var summary = await GenerateContent(prompt, _apiKey);

                if (string.IsNullOrWhiteSpace(summary))
                    return StatusCode(500, "Gemini API failed to generate summary.");

                return Ok(new
                {
                    
                    document_id = document.document_id,
                    title = document.Title,
                    summary = summary
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi xử lý tài liệu: {ex.Message}");
                return StatusCode(500, "Đã xảy ra lỗi trong quá trình xử lý tài liệu.");
            }
        }

        //Trò chuyện với Gemini
        [HttpPost("gemini/chat")]
        public async Task<IActionResult> ChatWithGemini([FromBody] AIChatRequest request)
        {
            if (string.IsNullOrEmpty(request.Message))
                return BadRequest("Message is required.");

            try
            {
                var prompt = request.Message; // Lấy prompt từ yêu cầu gửi đến API
                var response = await GenerateContent(prompt, _apiKey);

                if (string.IsNullOrWhiteSpace(response))
                    return StatusCode(500, "Gemini API failed to generate response.");

                return Ok(new
                {
                    message = response
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi xử lý tài liệu: {ex.Message}");
                return StatusCode(500, "Đã xảy ra lỗi trong quá trình xử lý tài liệu.");
            }
        }

        //Đổi Pdf sang text
        private string ExtractTextFromPdf(Stream stream)
        {
            using var document = new Aspose.Pdf.Document(stream);
            var textAbsorber = new Aspose.Pdf.Text.TextAbsorber();
            document.Pages.Accept(textAbsorber);
            return textAbsorber.Text;
        }

        public static async Task<string> GenerateContent(string prompt, string apiKey)
        {
            string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";
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

                    HttpResponseMessage response = await client.PostAsync(apiUrl, content);
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

    }
}
