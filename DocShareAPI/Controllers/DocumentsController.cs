using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using DocShareAPI.Data;
using DocShareAPI.Helpers;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using ELearningAPI.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DocShareAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DocumentsController> _logger;
        private readonly Cloudinary _cloudinary;
        private readonly TokenServices _tokenServices;
        private readonly long MAX_FILE_SIZE = 5 * 1024 * 1024; // 5Mb
        private readonly string[] ALLOWED_DOCUMENT_TYPES = {
            "application/pdf",
            "application/msword",
            "text/plain",
        };
        public DocumentsController(DocShareDbContext context, IConfiguration configuration, ILogger<DocumentsController> logger)
        {
            _logger = logger;
            _context = context;
            _configuration = configuration;
            //Lấy dữ liệu TokenKey từ biến môi trường
            string? tokenScretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
            if (string.IsNullOrEmpty(tokenScretKey))
            {
                Console.WriteLine("Không có khóa token được sử dụng trong LoginController");
                tokenScretKey = _configuration.GetValue<string>("TokenSecretKey");
            }
            if (tokenScretKey != null)
            {
                _tokenServices = new TokenServices(tokenScretKey);
            }
            try
            {
                var cloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME")
                    ?? configuration["Cloudinary:CloudName"];
                var apiKey = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY")
                    ?? configuration["Cloudinary:ApiKey"];
                var apiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET")
                    ?? configuration["Cloudinary:ApiSecret"];

                if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
                {
                    throw new ArgumentNullException("Cloudinary configuration is incomplete");
                }

                _cloudinary = new Cloudinary(new Account(cloudName, apiKey, apiSecret));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initializing Cloudinary: {ex}");
                throw;
            }
        }
        
        // GET: api/<DocumentsController>
        [HttpGet("documents")]
        public async Task<IActionResult> GetAllDocuments([FromQuery] PaginationParams paginationParams)
        {
            var decodedTokenResponse = await DecodeAndValidateToken();
            if (decodedTokenResponse == null)
            {
                return BadRequest(new { message = "Token không hợp lệ hoặc không tồn tại" });
            }
            //else if(decodedTokenResponse.roleID != "admin")
            //{
            //    return BadRequest(new { message = "Bạn không đủ quyền xem" });
            //}
            var query = _context.DOCUMENTS.AsQueryable();

            // Sử dụng extension method ToPagedListAsync
            var pagedData = await query
                .Select(d => new
                {
                    d.document_id,
                    d.Users.full_name,
                    d.Title,
                    d.thumbnail_url,
                    d.like_count,
                    d.is_public
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);
            return Ok(new
            {
                Data = pagedData,
                Pagination = new
                {
                    pagedData.CurrentPage,
                    pagedData.PageSize,
                    pagedData.TotalCount,
                    pagedData.TotalPages
                }
            });
        }

        // GET api/<DocumentsController>/5
        [HttpGet("document/{documentID}")]
        public async Task<IActionResult> GetDocumentByID(int documentID)
        {
            var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentID);
            if (document == null)
            {
                return BadRequest(new
                {
                    message = "Không có dữ liệu Tài liệu"
                });
            }
            return Ok(document);
        }

        // POST api/<DocumentsController>
        [HttpPost("upload-document")]
        public async Task<IActionResult> UploadDocument(IFormFile file)
        {
            try
            {
                var decodedTokenResponse = await DecodeAndValidateToken();
                if (decodedTokenResponse == null)
                {
                    return BadRequest(new { message = "Token không hợp lệ hoặc không tồn tại" });
                }
                if (!IsValidDocument(file, out string validationMessage))
                {
                    _logger.LogWarning(validationMessage);
                    return BadRequest(validationMessage);
                }

                string folder = "DocShare/Documents";

                using var stream = file.OpenReadStream();
                var uploadParams = new ImageUploadParams()
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folder,
                    UseFilename = true,
                    UniqueFilename = true,
                    Overwrite = false,
                    Tags = file.ContentType
                };

                var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                var newID = GenerateRandomCode.GenerateID();
                while (await _context.DOCUMENTS.AnyAsync(d => d.document_id == newID))
                {
                    newID = GenerateRandomCode.GenerateID();
                }
                Documents newDoc = new Documents
                {
                    document_id = newID,
                    user_id = decodedTokenResponse.userID,
                    Title = $"{newID}-{ConvertPdf.ConvertPdfTitle(file.FileName)}",
                    file_url = uploadResult.SecureUrl.ToString(),
                    public_id = uploadResult.PublicId,
                    thumbnail_url = ConvertPdf.ConvertPdfTitleToJpg(uploadResult.SecureUrl.ToString()),
                    file_size = Convert.ToInt32(file.Length),
                    file_type = uploadResult.Format,
                    uploaded_at = DateTime.UtcNow
                };
                _context.DOCUMENTS.Add(newDoc);
                await _context.SaveChangesAsync();
                return Ok(new
                {
                    message = "Tải tài liệu thành công",
                    success = true,
                    newDoc.document_id,
                    newDoc.Title
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error uploading document: {ex.Message}", ex);
                return StatusCode(500, "Internal server error occurred. Please try again.");
            }
        }


        // PUT api/<DocumentsController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<DocumentsController>/5
        [HttpDelete("delete-document")]
        public async Task<IActionResult> DeleteDocument(int documentID)
        {
            var decodedTokenResponse = await DecodeAndValidateToken();
            if (decodedTokenResponse == null)
            {
                return BadRequest(new { message = "Token không hợp lệ hoặc không tồn tại" });
            }

            // Lấy tài liệu
            var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentID);
            if (document == null)
            {
                return BadRequest(new { message = "Tài liệu không tồn tại" });
            }

            // Kiểm tra quyền sở hữu
            if (document.user_id != decodedTokenResponse.userID)
            {
                return BadRequest(new { message = "Bạn không phải chủ sở hữu tài liệu" });
            }

            return Ok(new
            {
                message = $"Xóa thành công: {document.Title}",
                success = true,
            });
        }


        // Kiểm tra tính hợp lệ của tài liệu được tải lên.
        private bool IsValidDocument(IFormFile file, out string validationMessage)
        {
            validationMessage = string.Empty;

            if (file == null || file.Length == 0)
            {
                validationMessage = "Please select a document to upload.";
                return false;
            }

            if (file.Length > MAX_FILE_SIZE)
            {
                validationMessage = $"Document size {file.Length} exceeds maximum allowed size of {MAX_FILE_SIZE}.";
                return false;
            }

            if (!ALLOWED_DOCUMENT_TYPES.Contains(file.ContentType))
            {
                validationMessage = $"Invalid document type: {file.ContentType}. Allowed types are: PDF, DOCX, TXT.";
                return false;
            }

            return true;
        }
        
        //Decode token và trả về Json
        private async Task<DecodedTokenResponse?> DecodeAndValidateToken()
        {
            // Lấy giá trị của cookie tên "token"
            if ( !Request.Cookies.TryGetValue("token", out string? cookieValue))
            {
                return null; // Có thể trả về null hoặc throw exception tùy cách bạn xử lý lỗi
            }

            // Decode token
            var decodedToken =  _tokenServices.DecodeToken(cookieValue);
            if (decodedToken == null)
            {
                return null;
            }

            // Parse JSON và kiểm tra hợp lệ
            var decodedTokenResponse =  JsonSerializer.Deserialize<DecodedTokenResponse>(decodedToken);
            return decodedTokenResponse;
        }

    }
}
