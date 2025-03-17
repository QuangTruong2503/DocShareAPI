using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using DocShareAPI.Data;
using DocShareAPI.DataTransferObject;
using DocShareAPI.Helpers;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using DocShareAPI.Services;
using ELearningAPI.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DocShareAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly ILogger<DocumentsController> _logger;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly long MAX_FILE_SIZE = 10 * 1024 * 1024; // 10Mb
        private readonly string[] ALLOWED_DOCUMENT_TYPES = {
            "application/pdf",
            "application/msword",
            "text/plain",
        };
        public DocumentsController(DocShareDbContext context, ICloudinaryService cloudinaryService, ILogger<DocumentsController> logger)
        {
            _logger = logger;
            _context = context;
            _cloudinaryService = cloudinaryService;
            var stopwatch = Stopwatch.StartNew();
            // Đoạn code cần đo
            _logger.LogInformation($"Thời gian thực thi: {stopwatch.ElapsedMilliseconds}ms");
        }
        
        // GET: api/<DocumentsController>
        [HttpGet("documents")]
        public async Task<IActionResult> GetAllDocuments([FromQuery] PaginationParams paginationParams)
        {
            var query = _context.DOCUMENTS.AsQueryable();
            // Sử dụng extension method ToPagedListAsync
            var pagedData = await query
                .Select(d => new
                {
                    d.document_id,
                    d.Users.full_name,
                    d.Title,
                    d.thumbnail_url,
                    d.is_public,
                    d.public_id
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

        // Lấy dữ liệu tài liệu thông qua ID
        [HttpGet("document/{documentID}")]
        public async Task<IActionResult> GetDocumentByID(int documentID)
        {
            // Lấy thông tin token từ HttpContext.Items (nếu có)
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;

            // Truy vấn document từ database
            var document = await _context.DOCUMENTS
                .Where(d => d.document_id == documentID)
                .Select(d => new
                {
                    d.document_id,
                    d.user_id,
                    d.Title,
                    d.Description,
                    d.file_url,
                    d.is_public,
                    d.download_count,
                    d.uploaded_at,
                    d.Users.full_name
                })
                .FirstOrDefaultAsync();

            // Kiểm tra document có tồn tại không
            if (document == null)
            {
                return NotFound(new { message = "Tài liệu không tồn tại." });
            }

            // Kiểm tra quyền truy cập
            if (!document.is_public && (decodedTokenResponse == null || decodedTokenResponse.userID != document.user_id))
            {
                return StatusCode(StatusCodes.Status403Forbidden, "Đây là tài liệu riêng tư!" );
            }

            // Trả về dữ liệu nếu có quyền
            return Ok(document);
        }

        //Lấy dữ liệu các Document đã được tải lên
        [HttpGet("my-uploaded-documents")]
        public async Task<IActionResult> GetMyUploadDocuments([FromQuery] PaginationParams paginationParams)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized(); // Không cần nữa vì middleware đã xử lý
            }
            var query = _context.DOCUMENTS.AsQueryable();

            // Sử dụng extension method ToPagedListAsync
            var pagedData = await query
                .Where(d => d.user_id == decodedTokenResponse.userID)
                .Select(d => new
                {
                    d.document_id,
                    d.Title,
                    d.Description,
                    d.thumbnail_url,
                    d.uploaded_at,
                    d.is_public
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);
            return Ok(new
            {
                Data = pagedData,
                Pagination = new
                {
                    pagedData.CurrentPage,
                    pagedData.TotalCount,
                    pagedData.TotalPages
                }
            });
        }

        [HttpPost("upload-document")]
        public async Task<IActionResult> UploadDocument(IFormFile file)
        {
            try
            {
                // Kiểm tra token
                var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
                if (decodedTokenResponse == null)
                {
                    return Unauthorized();
                }

                // Kiểm tra tính hợp lệ của tài liệu
                if (!IsValidDocument(file, out string validationMessage))
                {
                    _logger.LogWarning(validationMessage);
                    return BadRequest(validationMessage);
                }

                //Kiểm tra người dùng xác thực
                var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == decodedTokenResponse.userID);
                if (user != null && !user.is_verified)
                {
                    return StatusCode(403, "Tải lên tài liệu thất bại! Vui lòng xác thực tài khoản của bạn trong phần Cài đặt.");
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

                var uploadResult = await _cloudinaryService.Cloudinary.UploadAsync(uploadParams);

                // Kiểm tra xem upload có thành công không
                if (uploadResult == null || uploadResult.Error != null)
                {
                    var errorMessage = uploadResult?.Error?.Message ?? "Unknown error during upload";
                    _logger.LogError($"Cloudinary upload failed: {errorMessage}");
                    return StatusCode(500, $"Upload to Cloudinary failed: {errorMessage}");
                }

                // Tạo ID cho tài liệu
                var newID = GenerateRandomCode.GenerateID();
                while (await _context.DOCUMENTS.AnyAsync(d => d.document_id == newID))
                {
                    newID = GenerateRandomCode.GenerateID();
                }

                Documents newDoc = new Documents
                {
                    document_id = newID,
                    user_id = decodedTokenResponse.userID,
                    Title = $"{ConvertPdf.ConvertPdfTitle(file.FileName)}-{newID}",
                    file_url = uploadResult.SecureUrl.ToString(),
                    public_id = uploadResult.PublicId,
                    asset_id = uploadResult.AssetId,
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
                    newDoc.Title,
                    newDoc.thumbnail_url
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error uploading document: {ex.Message}", ex);
                return StatusCode(500, "Internal server error occurred. Please try again.");
            }
        }

        //Update title and description
        [HttpPut("update-title-description")]
        public async Task<IActionResult> UpdateTitle(DocumentUpdateAfterUploadDTO documents)
        {
            try
            {
                var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
                if (decodedTokenResponse == null)
                {
                    return Unauthorized(); // Không cần nữa vì middleware đã xử lý
                }
                var documemt = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documents.document_id);
                if (documemt == null)
                {
                    return BadRequest(new
                    {
                        message = "Tài liệu không tồn tại!"
                    });
                }
                if (documemt.user_id != decodedTokenResponse.userID)
                {
                    return BadRequest(new { message = "Bạn không phải người sở hữu tài liệu" });
                }
                documemt.Title = documents.title;
                documemt.Description = documents.description;
                documemt.is_public = documents.is_public;
                _context.DOCUMENTS.Update(documemt);
                await _context.SaveChangesAsync();
                return Ok(new
                {
                    message = "Lưu thông tin thành công!",
                    success = true,
                    documemt.document_id
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // DELETE api/<DocumentsController>/5
        [HttpDelete("delete-document")]
        public async Task<IActionResult> DeleteDocument(int documentID)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized(); // Không cần nữa vì middleware đã xử lý
            }

            // Lấy tài liệu
            var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentID);
            if (document == null)
            {
                return BadRequest(new { message = "Tài liệu không tồn tại" });
            }

            // Kiểm tra quyền sở hữu
            if (document.user_id != decodedTokenResponse.userID && decodedTokenResponse.roleID != "admin")
            {
                return BadRequest(new { message = "Bạn không phải chủ sở hữu tài liệu hoặc admin" });
            }

            try
            {
                var deleteParams = new DelResParams
                {
                    PublicIds = new List<string> { document.public_id },
                    Type = "upload",
                    ResourceType = ResourceType.Image
                };

                var result = await _cloudinaryService.Cloudinary.DeleteResourcesAsync(deleteParams);
                Console.WriteLine(result.JsonObj);
                // Kiểm tra kết quả từ Cloudinary
                if (result.Deleted != null && result.Deleted.ContainsKey(document.public_id))
                {
                    // Xóa document khỏi database
                    _context.DOCUMENTS.Remove(document);
                    await _context.SaveChangesAsync();

                    return Ok(new
                    {
                        message = $"Xóa thành công: {document.Title}",
                        success = true
                    });
                }

                return BadRequest(new { message = "Xóa tài liệu trên Cloudinary thất bại" });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    message = "Lỗi khi xóa tài liệu",
                    error = ex.Message
                });
            }
        }

        //Tải xuống tài liệu
        [HttpGet("download-document/{documentID}")]
        public async Task<IActionResult> DownloadDocument(int documentID)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized(); // Không cần nữa vì middleware đã xử lý
            }
            using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentID);
                    if (document == null)
                    {
                        return NotFound($"Không có tài liệu với ID: {documentID}");
                    }

                    document.download_count++;
                    _context.DOCUMENTS.Update(document);
                    await _context.SaveChangesAsync();

                    await transaction.CommitAsync();

                    var downloadURL = $"https://res-console.cloudinary.com/{_cloudinaryService.CloudName}/media_explorer_thumbnails/{document.asset_id}/download";
                    return Ok(new
                    {
                        success = true,
                        downloadURL = downloadURL
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(ex.Message);
                }
            }
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
                validationMessage = $"Document size {file.Length} exceeds maximum allowed size of {MAX_FILE_SIZE/1024/1024}MB.";
                return false;
            }

            if (!ALLOWED_DOCUMENT_TYPES.Contains(file.ContentType))
            {
                validationMessage = $"Invalid document type: {file.ContentType}. Allowed types are: PDF, DOCX, TXT.";
                return false;
            }

            return true;
        }        


    }
}
