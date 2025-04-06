using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using DocShareAPI.Data;
using DocShareAPI.DataTransferObject.Documents;
using DocShareAPI.Helpers;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;


namespace DocShareAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly ILogger<DocumentsController> _logger;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly long _maxFileSize;
        private readonly string[] _allowedDocumentTypes;

        public DocumentsController(DocShareDbContext context, ICloudinaryService cloudinaryService, ILogger<DocumentsController> logger, IConfiguration configuration)
        {
            _context = context;
            _cloudinaryService = cloudinaryService;
            _logger = logger;
            _maxFileSize = configuration.GetValue<long>("MaxFileSize", 10 * 1024 * 1024); // Default to 10MB
            _allowedDocumentTypes = configuration.GetSection("AllowedDocumentTypes")
                .Get<string[]>() ?? new[] 
                { "application/pdf", 
                  "application/msword", 
                  "text/plain", 
                  "application/vnd.openxmlformats-officedocument.wordprocessingml.document" 
                };
        }

        [HttpGet("documents")]
        public async Task<ActionResult> GetAllDocuments([FromQuery] PaginationParams paginationParams)
        {
            var query = _context.DOCUMENTS.AsQueryable();
            var pagedData = await query
                .Select(d => new
                {
                    d.document_id,
                    full_name = d.Users != null ? d.Users.full_name : string.Empty,
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


        [HttpGet("public/document/{documentID}")]
        public async Task<ActionResult> GetDocumentByID(int documentID)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;

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
                    d.file_size,
                    d.file_type,
                    d.uploaded_at,
                    full_name = d.Users != null ? d.Users.full_name : string.Empty
                })
                .FirstOrDefaultAsync();

            if (document == null)
            {
                return NotFound(new { message = "Document not found." });
            }

            if (!document.is_public)
            {
                if (decodedTokenResponse != null && (decodedTokenResponse.userID == document.user_id || decodedTokenResponse.roleID == "admin"))
                {
                    return Ok(document);
                }
                return NotFound(new {message = "Không thể truy cập vào tài liệu riêng tư!" });
            }

            return Ok(document);
        }
        //Lấy tài liệu theo search
        [HttpGet("public/search-documents")]
        public async Task<IActionResult> SearchDocuments([FromQuery] PaginationParams paginationParams ,[FromQuery] string search)
        {
            var query = from document in _context.DOCUMENTS
                        join user in _context.USERS on document.user_id equals user.user_id
                        join docCate in _context.DOCUMENT_CATEGORIES on document.document_id equals docCate.document_id into docCateGroup
                        from docCate in docCateGroup.DefaultIfEmpty() // LEFT JOIN

                        join category in _context.CATEGORIES on docCate.category_id equals category.category_id into categoryGroup
                        from category in categoryGroup.DefaultIfEmpty() // LEFT JOIN

                        join docTag in _context.DOCUMENT_TAGS on document.document_id equals docTag.document_id into docTagGroup
                        from docTag in docTagGroup.DefaultIfEmpty() // LEFT JOIN

                        join tag in _context.TAGS on docTag.tag_id equals tag.tag_id into tagGroup
                        from tag in tagGroup.DefaultIfEmpty() // LEFT JOIN

                        select new { document, user, category, tag };
            
            var documents = await query
                .Where(q => q.document.Title.ToLower().Contains(search.ToLower())
                    || q.category.Name.ToLower().Contains(search.ToLower())
                    || q.tag.Name.ToLower().Contains(search.ToLower())
                    || (q.document.Description != null && q.document.Description.ToLower().Contains(search.ToLower())))
                .Select(q => new
                {
                    q.document.document_id,
                    q.user.full_name,
                    q.document.Title,
                    q.document.thumbnail_url,
                    q.document.is_public,
                })
                .Distinct()
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);
            return Ok(new
            {
                documents = documents,
                Pagination = new
                {
                    documents.CurrentPage,
                    documents.TotalCount,
                    documents.TotalPages
                }
            });
        }
        [HttpGet("my-uploaded-documents")]
        public async Task<ActionResult> GetMyUploadDocuments(
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] string sortBy = "date",
            [FromQuery] bool? isPublic = null
            )
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized();
            }

            var query = _context.DOCUMENTS.AsQueryable()
                .Where(d => d.user_id == decodedTokenResponse.userID);
            if (isPublic != null)
            {
                query = query.Where(d => d.is_public == isPublic);
            }
            // Apply sorting
            query = (sortBy.ToLower()) switch
            {
                ("date") => query.OrderBy(d => d.uploaded_at),
                ("title") => query.OrderBy(d => d.Title),
                _ => query.OrderBy(d => d.uploaded_at) // Default to sort by date descending
            };
            var pageData = await query
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
                Data = pageData,
                Pagination = new
                {
                    pageData.CurrentPage,
                    pageData.TotalCount,
                    pageData.TotalPages
                }
            });
        }
        [HttpGet("public/documents-by-category")]
        public async Task<ActionResult> GetDocumentsByCategoryId([FromQuery]string categoryID, [FromQuery] PaginationParams paginationParams)
        {
            var query = from document in _context.DOCUMENTS
                        join user in _context.USERS on document.user_id equals user.user_id
                        join docCate in _context.DOCUMENT_CATEGORIES on document.document_id equals docCate.document_id
                        join cate in _context.CATEGORIES on docCate.category_id equals cate.category_id
                        where docCate.category_id == categoryID || cate.parent_id == categoryID
                        select new
                        {
                            document.document_id,
                            document.Title,
                            user.full_name,
                            document.thumbnail_url,
                            document.is_public,
                            document.uploaded_at
                        };

            var pagedData = await query.ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(new
            {
                documents = pagedData,
                Pagination = new
                {
                    pagedData.CurrentPage,
                    pagedData.PageSize,
                    pagedData.TotalCount,
                    pagedData.TotalPages
                }
            });
        }


        [HttpPost("upload-document")]
        public async Task<ActionResult> UploadDocument(IFormFile file)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized("Invalid or missing authentication token");
            }

            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("No file provided for upload");
                return BadRequest("No file provided for upload");
            }

            if (!IsValidDocument(file, out string validationMessage))
            {
                _logger.LogWarning($"File validation failed: {validationMessage}");
                return BadRequest(validationMessage);
            }

            var user = await _context.USERS
                .FirstOrDefaultAsync(u => u.user_id == decodedTokenResponse.userID);

            if (user == null)
            {
                _logger.LogWarning($"User not found: {decodedTokenResponse.userID}");
                return NotFound("User not found");
            }

            if (!user.is_verified)
            {
                return Forbid("Upload failed! Please verify your account in the settings.");
            }

            IFormFile fileToUpload = file;
            MemoryStream pdfStream = null;

            try
            {
                // Handle DOCX to PDF conversion
                if (Path.GetExtension(file.FileName).ToLower() == ".docx")
                {
                    using (var inputStream = file.OpenReadStream())
                    {
                        var doc = new Aspose.Words.Document(inputStream);
                        pdfStream = new MemoryStream();
                        doc.Save(pdfStream, Aspose.Words.SaveFormat.Pdf);
                        pdfStream.Position = 0;

                        fileToUpload = new FormFile(
                            pdfStream,
                            0,
                            pdfStream.Length,
                            file.Name,
                            Path.ChangeExtension(file.FileName, ".pdf"))
                        {
                            Headers = file.Headers,
                            ContentType = "application/pdf"
                        };
                    }
                }

                // Upload to Cloudinary
                var uploadResult = await UploadToCloudinary(fileToUpload);
                if (uploadResult == null || uploadResult.Error != null)
                {
                    var errorMessage = uploadResult?.Error?.Message ?? "Unknown error during upload";
                    _logger.LogError($"Cloudinary upload failed: {errorMessage}");
                    return StatusCode(500, $"Upload failed: {errorMessage}");
                }

                var newDoc = await CreateDocumentRecord(fileToUpload, decodedTokenResponse.userID, uploadResult);

                return Ok(new
                {
                    message = "Document uploaded successfully",
                    success = true,
                    newDoc.document_id,
                    newDoc.Title,
                    newDoc.thumbnail_url,
                    uploadResult
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Upload process failed: {ex.Message}");
                return StatusCode(500, "An error occurred during document upload");
            }
            finally
            {
                // Clean up the MemoryStream if it was created
                pdfStream?.Dispose();
            }
        }

        //Cập nhật tài liệu với document_id
        [HttpPut("update-document")]
        public async Task<ActionResult> UpdateDocument(DocumentUpdateDTO documentUpdate)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized();
            }
            try
            {
                var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentUpdate.document_id);
                if (document == null)
                {
                    return NotFound(new { message = "Document not found" });
                }
                if (document.user_id != decodedTokenResponse.userID && decodedTokenResponse.roleID != "admin")
                {
                    return BadRequest(new { message = "You are not the owner of this document or an admin" });
                }
                document.Title = documentUpdate.title;
                document.Description = documentUpdate.description;
                document.is_public = documentUpdate.is_public;
                await _context.SaveChangesAsync();
                return Ok(new
                {
                    data = document,
                    message = "Document updated successfully",
                    success = true
                });
            }
            catch(Exception ex)
            {
                return BadRequest(new {message = ex.Message });
            }
        }

        [HttpPut("update-document-after-upload")]
        public async Task<ActionResult> UpdateTitle(DocumentUpdateAfterUploadDTO documents)
        {
            // Kiểm tra token sớm và trả về ngay
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
            {
                return Unauthorized();
            }

            // Tìm document với điều kiện user_id ngay từ đầu
            var document = await _context.DOCUMENTS
                .FirstOrDefaultAsync(d => d.document_id == documents.document_id
                    && d.user_id == decodedToken.userID);

            if (document == null)
            {
                return BadRequest(new { message = "Document not found or you don't have permission!" });
            }

            // Cập nhật các thuộc tính cơ bản
            document.Title = documents.title;
            document.Description = documents.description;
            document.is_public = documents.is_public;

            // Xử lý tags hiệu quả hơn
            if (documents.tags?.Any() == true)
            {
                var tagIdsToAdd = new List<string>();

                foreach (var tag in documents.tags)
                {
                    string tagId = RemoveDiacritics(tag.Name);
                    tagIdsToAdd.Add(tagId);

                    if (!await _context.TAGS.AnyAsync(t => t.tag_id == tagId))
                    {
                        _context.TAGS.Add(new Tags
                        {
                            tag_id = tagId,
                            Name = tag.Name
                        });
                    }
                }

                // Thêm DocumentTags một lần
                _context.DOCUMENT_TAGS.AddRange(tagIdsToAdd.Select(tagId => new DocumentTags
                {
                    document_id = document.document_id,
                    tag_id = tagId
                }));
            }

            // Lưu tất cả thay đổi trong một lần
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Thông tin tài liệu đã được lưu!",
                success = true,
                document_id = document.document_id
            });
        }

        [HttpDelete("delete-document")]
        public async Task<ActionResult> DeleteDocument(int documentID)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized();
            }

            var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentID);
            if (document == null)
            {
                return BadRequest(new { message = "Document not found" });
            }

            if (document.user_id != decodedTokenResponse.userID && decodedTokenResponse.roleID != "admin")
            {
                return BadRequest(new { message = "You are not the owner of this document or an admin" });
            }

            var result = await DeleteFromCloudinary(document.public_id);
            if (result.Deleted != null && result.Deleted.ContainsKey(document.public_id))
            {
                _context.DOCUMENTS.Remove(document);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = $"Successfully deleted: {document.Title}",
                    success = true
                });
            }

            return BadRequest(new { message = "Failed to delete document from Cloudinary" });
        }

        [HttpGet("download-document/{documentID}")]
        public async Task<ActionResult> DownloadDocument(int documentID)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized();
            }

            try
            {
                var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentID);
                if (document == null)
                {
                    return NotFound($"No document found with ID: {documentID}");
                }

                // Cập nhật số lượt tải xuống
                document.download_count++;
                await _context.SaveChangesAsync();  // Không cần transaction

                var downloadURL = $"https://res-console.cloudinary.com/{_cloudinaryService.CloudName}/media_explorer_thumbnails/{document.asset_id}/download";
                return Ok(new
                {
                    success = true,
                    downloadURL
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }


        private bool IsValidDocument(IFormFile file, out string validationMessage)
        {
            validationMessage = string.Empty;

            if (file == null || file.Length == 0)
            {
                validationMessage = "Please select a document to upload.";
                return false;
            }

            if (file.Length > _maxFileSize)
            {
                validationMessage = $"Document size {file.Length} exceeds maximum allowed size of {_maxFileSize / 1024 / 1024}MB.";
                return false;
            }

            if (!_allowedDocumentTypes.Contains(file.ContentType))
            {
                validationMessage = $"Invalid document type: {file.ContentType}. Allowed types are: PDF, DOCX, TXT.";
                return false;
            }

            return true;
        }
        //Upload tài liêu
        private async Task<ImageUploadResult> UploadToCloudinary(IFormFile file)
        {
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

            return await _cloudinaryService.Cloudinary.UploadAsync(uploadParams);
        }

        //Thêm bản ghi mới của tài liệu trong Cloudinary
        private async Task<Documents> CreateDocumentRecord(IFormFile file, Guid userId, ImageUploadResult uploadResult)
        {
            var newID = GenerateRandomCode.GenerateID();
            while (await _context.DOCUMENTS.AnyAsync(d => d.document_id == newID))
            {
                newID = GenerateRandomCode.GenerateID();
            }

            var newDoc = new Documents
            {
                document_id = newID,
                user_id = userId,
                Title = $"{ConvertPdf.ConvertPdfTitle(file.FileName)}-{newID}",
                file_url = uploadResult.SecureUrl.ToString(),
                public_id = uploadResult.PublicId,
                asset_id = uploadResult.AssetId,
                thumbnail_url = ConvertPdf.ConvertPdfTitleToJpg(uploadResult.SecureUrl.ToString()),
                file_size = Convert.ToInt32(file.Length),
                file_type = uploadResult.Format,
                pages = uploadResult.Pages,
                uploaded_at = DateTime.UtcNow
            };

            _context.DOCUMENTS.Add(newDoc);
            await _context.SaveChangesAsync();
            return newDoc;
        }

        private async Task<DelResResult> DeleteFromCloudinary(string publicId)
        {
            var deleteParams = new DelResParams
            {
                PublicIds = new List<string> { publicId },
                Type = "upload",
                ResourceType = ResourceType.Image
            };

            return await _cloudinaryService.Cloudinary.DeleteResourcesAsync(deleteParams);
        }
        public static string RemoveDiacritics(string text)
        {
            string normalized = text.Normalize(NormalizationForm.FormD);
            Regex regex = new Regex(@"\p{M}");
            string result = regex.Replace(normalized, "").Normalize(NormalizationForm.FormC);
            return result.ToLower().Replace(" ", ""); // Loại bỏ khoảng trắng
        }
    }

}
