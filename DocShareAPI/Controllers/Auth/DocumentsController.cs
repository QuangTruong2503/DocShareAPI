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
using System.Text;
using System.Text.RegularExpressions;


namespace DocShareAPI.Controllers.Auth
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly ILogger<DocumentsController> _logger;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly INotificationService _notificationService;
        private readonly IFolderPermissionService _folderPermissionService;
        private readonly long _maxFileSize;
        private readonly string[] _allowedDocumentTypes;
        private readonly HttpClient _httpClient;

        public DocumentsController(
            DocShareDbContext context,
            ICloudinaryService cloudinaryService,
            INotificationService notificationService,
            IFolderPermissionService folderPermissionService,
            ILogger<DocumentsController> logger,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _cloudinaryService = cloudinaryService;
            _notificationService = notificationService;
            _folderPermissionService = folderPermissionService;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
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
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized();
            }

            if (decodedTokenResponse.roleID != "admin")
            {
                return Forbid();
            }

            var query = _context.DOCUMENTS.AsNoTracking();
            var pagedData = await query
                .OrderByDescending(d => d.uploaded_at)
                .Select(d => new
                {
                    d.document_id,
                    full_name = d.Users != null ? d.Users.full_name : string.Empty,
                    d.Title,
                    d.Description,
                    d.thumbnail_url,
                    d.is_public,
                    d.uploaded_at,
                    d.download_count
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

            var query = _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.user_id == decodedTokenResponse.userID);
            if (isPublic != null)
            {
                query = query.Where(d => d.is_public == isPublic);
            }
            // Apply sorting
            query = sortBy.ToLower() switch
            {
                "date" => query.OrderByDescending(d => d.uploaded_at),
                "title" => query.OrderBy(d => d.Title),
                _ => query.OrderByDescending(d => d.uploaded_at)
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

        [HttpPost("upload-document")]
        public async Task<ActionResult> UploadDocument(IFormFile file)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized("Token xác thực không hợp lệ hoặc bị thiếu.");
            }

            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("Chưa cung cấp file để tải lên.");
                return BadRequest("Chưa cung cấp file để tải lên.");
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
                _logger.LogWarning($"Không tìm thấy người dùng.: {decodedTokenResponse.userID}");
                return NotFound("Không tìm thấy người dùng.");
            }

            if (!user.is_verified)
            {
                return Forbid("Tải lên thất bại! Vui lòng xác thực tài khoản trong phần cài đặt.");
            }

            IFormFile fileToUpload = file;
            MemoryStream? pdfStream = null;
            ImageUploadResult? uploadResult = null;

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
                uploadResult = await UploadToCloudinary(fileToUpload);
                if (uploadResult == null || uploadResult.Error != null)
                {
                    var errorMessage = uploadResult?.Error?.Message ?? "Lỗi không xác định trong quá trình tải lên";
                    _logger.LogError($"Cloudinary upload failed: {errorMessage}");
                    return StatusCode(500, $"Tải lên thất bại: {errorMessage}");
                }

                Documents? newDoc = null;
                var strategy = _context.Database.CreateExecutionStrategy();

                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();
                    newDoc = await CreateDocumentRecord(fileToUpload, decodedTokenResponse.userID, uploadResult);

                    await _notificationService.CreateAsync(
                        recipientUserId: decodedTokenResponse.userID,
                        type: "DOCUMENT_UPLOADED",
                        title: "Tải tài liệu thành công",
                        message: $"Tài liệu \"{newDoc.Title}\" đã được tải lên.",
                        relatedDocumentId: newDoc.document_id,
                        targetUrl: $"/documents/{newDoc.document_id}");

                    await transaction.CommitAsync();
                });

                return Ok(new
                {
                    message = "Tải tài liệu lên thành công.",
                    success = true,
                    newDoc!.document_id,
                    title = newDoc.Title,
                    newDoc.thumbnail_url,
                    newDoc.file_url
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Upload process failed: {ex.Message}");
                if (uploadResult?.PublicId != null)
                {
                    await DeleteFromCloudinary(uploadResult.PublicId);
                }
                return StatusCode(500, "Đã xảy ra lỗi trong quá trình tải tài liệu lên.");
            }
            finally
            {
                // Clean up the MemoryStream if it was created
                pdfStream?.Dispose();
            }
        }

        [HttpPost("/api/folders/{folderId:int}/upload-document")]
        [HttpPost("upload-document-to-folder/{folderId:int}")]
        public async Task<ActionResult> UploadDocumentToFolder(int folderId, IFormFile file)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
            {
                return Unauthorized("Token xác thực không hợp lệ hoặc bị thiếu.");
            }

            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("Chưa cung cấp file để tải lên thư mục.");
                return BadRequest(new { success = false, code = "FILE_REQUIRED", message = "Vui lòng chọn tài liệu để tải lên." });
            }

            if (!IsValidDocument(file, out string validationMessage))
            {
                _logger.LogWarning($"Folder upload file validation failed: {validationMessage}");
                return BadRequest(new { success = false, code = "INVALID_FILE", message = validationMessage });
            }

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == decodedToken.userID);
            if (user == null)
            {
                _logger.LogWarning($"Không tìm thấy người dùng.: {decodedToken.userID}");
                return NotFound(new { success = false, code = "USER_NOT_FOUND", message = "Không tìm thấy người dùng." });
            }

            if (!user.is_verified)
            {
                return Forbid("Tải lên thất bại! Vui lòng xác thực tài khoản trong phần cài đặt.");
            }

            var folder = await _context.FOLDERS
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.folder_id == folderId);

            if (folder == null)
            {
                return NotFound(new { success = false, code = "FOLDER_NOT_FOUND", message = "Không tìm thấy thư mục." });
            }

            if (!await _folderPermissionService.CanAddDocumentToFolderAsync(decodedToken.userID, folderId))
            {
                return Forbid();
            }

            IFormFile fileToUpload = file;
            MemoryStream? pdfStream = null;
            ImageUploadResult? uploadResult = null;

            try
            {
                if (Path.GetExtension(file.FileName).ToLowerInvariant() == ".docx")
                {
                    using var inputStream = file.OpenReadStream();
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

                uploadResult = await UploadToCloudinary(fileToUpload);
                if (uploadResult == null || uploadResult.Error != null)
                {
                    var errorMessage = uploadResult?.Error?.Message ?? "Lỗi không xác định trong quá trình tải lên";
                    _logger.LogError($"Cloudinary folder upload failed: {errorMessage}");
                    return StatusCode(500, new { success = false, code = "CLOUDINARY_UPLOAD_FAILED", message = $"Tải lên thất bại: {errorMessage}" });
                }

                Documents? newDoc = null;
                FolderDocuments? folderDocument = null;
                var strategy = _context.Database.CreateExecutionStrategy();

                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();

                    newDoc = await CreateDocumentRecord(fileToUpload, decodedToken.userID, uploadResult);
                    folderDocument = new FolderDocuments
                    {
                        folder_id = folder.folder_id,
                        document_id = newDoc.document_id,
                        added_by_user_id = decodedToken.userID,
                        added_at = DateTime.UtcNow
                    };

                    _context.FOLDER_DOCUMENTS.Add(folderDocument);
                    await _context.SaveChangesAsync();

                    await _notificationService.CreateAsync(
                        recipientUserId: decodedToken.userID,
                        type: "DOCUMENT_UPLOADED",
                        title: "Tải tài liệu vào thư mục thành công",
                        message: $"Tài liệu \"{newDoc.Title}\" đã được tải lên thư mục \"{folder.name}\".",
                        relatedDocumentId: newDoc.document_id,
                        relatedFolderId: folder.folder_id,
                        targetUrl: $"/library/folders/{folder.folder_id}/documents",
                        metadata: new { folder_id = folder.folder_id, document_id = newDoc.document_id });

                    if (folder.owner_user_id != decodedToken.userID)
                    {
                        await _notificationService.CreateAsync(
                            recipientUserId: folder.owner_user_id,
                            actorUserId: decodedToken.userID,
                            type: "folder_document_added",
                            title: "Có tài liệu mới trong thư mục",
                            message: $"Tài liệu \"{newDoc.Title}\" đã được thêm vào thư mục \"{folder.name}\".",
                            relatedDocumentId: newDoc.document_id,
                            relatedFolderId: folder.folder_id,
                            targetUrl: $"/library/folders/{folder.folder_id}/documents",
                            metadata: new { folder_id = folder.folder_id, document_id = newDoc.document_id });
                    }

                    await transaction.CommitAsync();
                });

                return Ok(new
                {
                    success = true,
                    message = "Tải tài liệu vào thư mục thành công.",
                    document_id = newDoc!.document_id,
                    folder_id = folder.folder_id,
                    added_to_folder = true,
                    title = newDoc.Title,
                    thumbnail_url = newDoc.thumbnail_url,
                    file_url = newDoc.file_url,
                    file_type = newDoc.file_type,
                    file_size = newDoc.file_size,
                    pages = newDoc.pages,
                    uploaded_at = newDoc.uploaded_at,
                    folder_document = new
                    {
                        folderDocument!.folder_id,
                        folderDocument.document_id,
                        folderDocument.added_by_user_id,
                        folderDocument.added_at
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Folder upload process failed: {ex.Message}");
                if (uploadResult?.PublicId != null)
                {
                    await DeleteFromCloudinary(uploadResult.PublicId);
                }

                return StatusCode(500, new { success = false, code = "FOLDER_DOCUMENT_UPLOAD_FAILED", message = "Đã xảy ra lỗi trong quá trình tải tài liệu vào thư mục." });
            }
            finally
            {
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
                    return NotFound(new { message = "Không tìm thấy tài liệu." });
                }
                if (document.user_id != decodedTokenResponse.userID && decodedTokenResponse.roleID != "admin")
                {
                    return Forbid();
                }
                var wasPublic = document.is_public;
                document.Title = documentUpdate.title;
                document.Description = documentUpdate.description;
                document.is_public = documentUpdate.is_public;
                await _context.SaveChangesAsync();

                if (!wasPublic && document.is_public)
                {
                    await NotifyFollowersAboutPublishedDocument(document);
                }

                return Ok(new
                {
                    data = new
                    {
                        document.document_id,
                        document.Title,
                        document.Description,
                        document.thumbnail_url,
                        document.uploaded_at,
                        document.is_public
                    },
                    message = "Cập nhật tài liệu thành công.",
                    success = true
                });
            }
            catch(Exception ex)
            {
                return BadRequest(new {message = ex.Message });
            }
        }

        [HttpPut("update-document-after-upload")]
        public async Task<ActionResult> UpdateDocumentAfterUpload(
    [FromBody] DocumentUpdateAfterUploadDTO documents)
        {
            //Kiểm tra token
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
            {
                return Unauthorized();
            }

            //Lấy document + quyền sở hữu
            var document = await _context.DOCUMENTS
                .Include(d => d.DocumentTags)
                .Include(d => d.DocumentCategories)
                .FirstOrDefaultAsync(d =>
                    d.document_id == documents.document_id &&
                    d.user_id == decodedToken.userID);

            if (document == null)
            {
                return NotFound(new
                {
                    message = "Không tìm thấy tài liệu hoặc bạn không có quyền truy cập!"
                });
            }

            //Update thông tin cơ bản
            var wasPublic = document.is_public;
            document.Title = documents.title;
            document.Description = documents.description;
            document.is_public = documents.is_public;

            var folderMetadataAllowed = await _context.FOLDER_DOCUMENTS
                .AnyAsync(fd => fd.document_id == document.document_id);

            if (documents.folder_id.HasValue)
            {
                var folderExists = await _context.FOLDERS.AnyAsync(f => f.folder_id == documents.folder_id.Value);
                if (!folderExists)
                {
                    return NotFound(new { success = false, code = "FOLDER_NOT_FOUND", message = "Không tìm thấy thư mục." });
                }

                var existingFolderId = await _context.FOLDER_DOCUMENTS
                    .Where(fd => fd.document_id == document.document_id)
                    .Select(fd => (int?)fd.folder_id)
                    .FirstOrDefaultAsync();

                if (existingFolderId.HasValue && existingFolderId.Value != documents.folder_id.Value)
                {
                    return Conflict(new
                    {
                        success = false,
                        code = "DOCUMENT_ALREADY_IN_FOLDER",
                        message = "Tài liệu đã nằm trong một thư mục khác.",
                        folder_id = existingFolderId.Value
                    });
                }

                if (!existingFolderId.HasValue)
                {
                    if (!await _folderPermissionService.CanAddDocumentToFolderAsync(decodedToken.userID, documents.folder_id.Value))
                    {
                        return Forbid();
                    }

                    _context.FOLDER_DOCUMENTS.Add(new FolderDocuments
                    {
                        folder_id = documents.folder_id.Value,
                        document_id = document.document_id,
                        added_by_user_id = decodedToken.userID,
                        added_at = DateTime.UtcNow
                    });
                }

                folderMetadataAllowed = true;
            }

            // TAGS
            // =========================
            if (documents.tags != null)
            {
                // Xóa tags cũ
                _context.DOCUMENT_TAGS.RemoveRange(document.DocumentTags ?? Enumerable.Empty<DocumentTags>());

                var tagNames = documents.tags
                    .Select(t => t.Name.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Distinct()
                    .ToList();

                var tagIds = tagNames
                    .Select(RemoveDiacritics)
                    .ToList();

                // Lấy các tag đã tồn tại
                var existingTags = await _context.TAGS
                    .Where(t => tagIds.Contains(t.tag_id))
                    .Select(t => t.tag_id)
                    .ToListAsync();

                // Tạo tag mới nếu chưa tồn tại
                var newTags = tagIds
                    .Where(id => !existingTags.Contains(id))
                    .Select(id => new Tags
                    {
                        tag_id = id,
                        Name = tagNames[tagIds.IndexOf(id)]
                    });

                await _context.TAGS.AddRangeAsync(newTags);

                // Thêm DocumentTags
                var documentTags = tagIds.Select(tagId => new DocumentTags
                {
                    document_id = document.document_id,
                    tag_id = tagId
                });

                await _context.DOCUMENT_TAGS.AddRangeAsync(documentTags);
            }

            // =========================
            // CATEGORIES
            // =========================
            if (documents.categories != null)
            {
                // Xóa categories cũ
                _context.DOCUMENT_CATEGORIES.RemoveRange(document.DocumentCategories);

                var categoryIds = documents.categories
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                if (categoryIds.Count == 0 && !folderMetadataAllowed)
                {
                    return BadRequest(new
                    {
                        success = false,
                        code = "CATEGORY_REQUIRED",
                        message = "Vui lòng chọn ít nhất một danh mục cho tài liệu không thuộc thư mục."
                    });
                }

                var validCategoryIds = categoryIds.Count == 0
                    ? new List<string>()
                    : await _context.CATEGORIES
                        .Where(c => categoryIds.Contains(c.category_id))
                        .Select(c => c.category_id)
                        .ToListAsync();

                var invalidCategoryIds = categoryIds.Except(validCategoryIds).ToList();
                if (invalidCategoryIds.Count > 0)
                {
                    return UnprocessableEntity(new
                    {
                        success = false,
                        code = "INVALID_CATEGORY",
                        message = "Một hoặc nhiều danh mục không hợp lệ.",
                        invalid_category_ids = invalidCategoryIds
                    });
                }

                var documentCategories = validCategoryIds.Select(catId => new DocumentCategories
                {
                    document_id = document.document_id,
                    category_id = catId
                });

                await _context.DOCUMENT_CATEGORIES.AddRangeAsync(documentCategories);
            }

            // Save 1 lần
            await _context.SaveChangesAsync();

            if (!wasPublic && document.is_public)
            {
                await NotifyFollowersAboutPublishedDocument(document);
            }

            return Ok(new
            {
                success = true,
                message = "Thông tin tài liệu đã được lưu!",
                document_id = document.document_id
            });
        }


        [HttpDelete("delete-document")]
        public async Task<ActionResult> DeleteDocument(
            [FromBody] DeleteDocumentsDTO? request,
            [FromQuery] int? documentID = null,
            [FromQuery] List<int>? document_ids = null)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized();
            }

            var requestedIds = new List<int>();
            if (request?.document_ids != null)
                requestedIds.AddRange(request.document_ids);
            if (document_ids != null)
                requestedIds.AddRange(document_ids);
            if (documentID.HasValue)
                requestedIds.Add(documentID.Value);

            requestedIds = requestedIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (requestedIds.Count == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    code = "DOCUMENT_IDS_REQUIRED",
                    message = "Vui lòng truyền danh sách document_ids cần xóa.",
                    expected_body = new { document_ids = new[] { 10, 11, 12 } },
                    legacy_query = "/api/Documents/delete-document?documentID=10"
                });
            }

            var documents = await _context.DOCUMENTS
                .Where(d => requestedIds.Contains(d.document_id))
                .ToListAsync();

            var foundIds = documents.Select(d => d.document_id).ToHashSet();
            var notFoundIds = requestedIds.Where(id => !foundIds.Contains(id)).ToList();
            var isAdmin = string.Equals(decodedTokenResponse.roleID, "admin", StringComparison.OrdinalIgnoreCase);
            var forbiddenIds = documents
                .Where(d => !isAdmin && d.user_id != decodedTokenResponse.userID)
                .Select(d => d.document_id)
                .ToList();

            var deletableDocuments = documents
                .Where(d => isAdmin || d.user_id == decodedTokenResponse.userID)
                .ToList();

            if (deletableDocuments.Count == 0)
            {
                var statusCode = forbiddenIds.Count > 0 ? StatusCodes.Status403Forbidden : StatusCodes.Status404NotFound;
                return StatusCode(statusCode, new
                {
                    success = false,
                    code = forbiddenIds.Count > 0 ? "DOCUMENT_DELETE_FORBIDDEN" : "DOCUMENT_NOT_FOUND",
                    message = forbiddenIds.Count > 0
                        ? "Bạn không có quyền xóa các tài liệu đã chọn."
                        : "Không tìm thấy tài liệu nào để xóa.",
                    requested_document_ids = requestedIds,
                    deleted_document_ids = Array.Empty<int>(),
                    not_found_document_ids = notFoundIds,
                    forbidden_document_ids = forbiddenIds
                });
            }

            var remoteDeleteFailures = new List<object>();
            var deletedDocuments = new List<Documents>();

            foreach (var document in deletableDocuments)
            {
                if (string.IsNullOrWhiteSpace(document.public_id))
                {
                    deletedDocuments.Add(document);
                    continue;
                }

                try
                {
                    var result = await DeleteFromCloudinary(document.public_id);
                    if (result.Deleted != null && result.Deleted.ContainsKey(document.public_id))
                    {
                        deletedDocuments.Add(document);
                        continue;
                    }

                    remoteDeleteFailures.Add(new
                    {
                        document_id = document.document_id,
                        title = document.Title,
                        reason = "Không thể xóa tài liệu khỏi Cloudinary."
                    });
                }
                catch (Exception ex)
                {
                    remoteDeleteFailures.Add(new
                    {
                        document_id = document.document_id,
                        title = document.Title,
                        reason = ex.Message
                    });
                }
            }

            if (deletedDocuments.Count > 0)
            {
                var deletedIds = deletedDocuments.Select(d => d.document_id).ToList();
                await DeleteDocumentRelations(deletedIds);
                _context.DOCUMENTS.RemoveRange(deletedDocuments);
                await _context.SaveChangesAsync();
            }

            var deletedDocumentData = deletedDocuments.Select(d => new
            {
                d.document_id,
                title = d.Title,
                d.thumbnail_url
            }).ToList();

            var hasFailures = notFoundIds.Count > 0 || forbiddenIds.Count > 0 || remoteDeleteFailures.Count > 0;

            return Ok(new
            {
                success = !hasFailures,
                partial_success = deletedDocuments.Count > 0 && hasFailures,
                message = hasFailures
                    ? "Đã xử lý yêu cầu xóa tài liệu, một số tài liệu không thể xóa."
                    : "Đã xóa tài liệu thành công.",
                requested_count = requestedIds.Count,
                deleted_count = deletedDocuments.Count,
                failed_count = requestedIds.Count - deletedDocuments.Count,
                requested_document_ids = requestedIds,
                deleted_document_ids = deletedDocuments.Select(d => d.document_id).ToList(),
                deleted_documents = deletedDocumentData,
                not_found_document_ids = notFoundIds,
                forbidden_document_ids = forbiddenIds,
                failed_documents = remoteDeleteFailures
            });
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
                    return NotFound($"Không tìm thấy tài liệu có ID: {documentID}");
                }

                bool canDownload = document.is_public ||
                    document.user_id == decodedTokenResponse.userID ||
                    decodedTokenResponse.roleID == "admin";

                if (!canDownload)
                {
                    return Forbid();
                }

                var result = await _cloudinaryService.Cloudinary.GetResourceByAssetIdAsync(document.asset_id);
                if (result == null || string.IsNullOrEmpty(result.SecureUrl))
                {
                    return NotFound("Tài liệu không tồn tại.");
                }

                var fileBytes = await _httpClient.GetByteArrayAsync(result.SecureUrl);
                var fileName = document.Title;
                var contentType = "application/pdf"; // Hoặc loại MIME phù hợp với tài liệu của bạn
                // Cập nhật số lượt tải xuống
                document.download_count++;
                await _context.SaveChangesAsync();  // Không cần transaction

                if (IsDownloadMilestone(document.download_count) && document.user_id != decodedTokenResponse.userID)
                {
                    await _notificationService.CreateAsync(
                        recipientUserId: document.user_id,
                        actorUserId: decodedTokenResponse.userID,
                        type: "DOCUMENT_DOWNLOAD_MILESTONE",
                        title: "Tài liệu đạt mốc lượt tải",
                        message: $"Tài liệu \"{document.Title}\" đã đạt {document.download_count} lượt tải.",
                        relatedDocumentId: document.document_id,
                        targetUrl: $"/documents/{document.document_id}",
                        metadata: new { download_count = document.download_count });
                }

                return File(fileBytes, contentType, fileName);
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
                validationMessage = "Vui lòng chọn tài liệu để tải lên.";
                return false;
            }

            if (file.Length > _maxFileSize)
            {
                validationMessage = $"Dung lượng tài liệu {file.Length} vượt quá giới hạn cho phép {_maxFileSize / 1024 / 1024}MB.";
                return false;
            }

            if (!_allowedDocumentTypes.Any(t => string.Equals(t, file.ContentType, StringComparison.OrdinalIgnoreCase)))
            {
                validationMessage = $"Loại tài liệu không hợp lệ: {file.ContentType}. Các loại được phép: PDF, DOCX, TXT.";
                return false;
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".txt" };
            if (!allowedExtensions.Contains(extension))
            {
                validationMessage = "Phần mở rộng tài liệu không hợp lệ. Các phần mở rộng được phép: PDF, DOC, DOCX, TXT.";
                return false;
            }

            if (!HasValidFileSignature(file, extension))
            {
                validationMessage = "Nội dung tài liệu không khớp với loại file đã tải lên.";
                return false;
            }

            return true;
        }

        private static bool HasValidFileSignature(IFormFile file, string extension)
        {
            using var stream = file.OpenReadStream();
            var header = new byte[8];
            var bytesRead = stream.Read(header, 0, header.Length);

            return extension switch
            {
                ".pdf" => bytesRead >= 4
                    && header[0] == 0x25
                    && header[1] == 0x50
                    && header[2] == 0x44
                    && header[3] == 0x46,
                ".docx" => bytesRead >= 4
                    && header[0] == 0x50
                    && header[1] == 0x4B
                    && header[2] == 0x03
                    && header[3] == 0x04,
                ".doc" => bytesRead >= 8
                    && header[0] == 0xD0
                    && header[1] == 0xCF
                    && header[2] == 0x11
                    && header[3] == 0xE0
                    && header[4] == 0xA1
                    && header[5] == 0xB1
                    && header[6] == 0x1A
                    && header[7] == 0xE1,
                ".txt" => bytesRead > 0 && !header.Take(bytesRead).Contains((byte)0x00),
                _ => false
            };
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

        //Xóa bản ghi tài liệu trong Cloudinary
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

        private async Task DeleteDocumentRelations(IEnumerable<int> documentIds)
        {
            var ids = documentIds.Distinct().ToList();
            if (ids.Count == 0)
                return;

            await _context.FOLDER_DOCUMENTS.Where(fd => ids.Contains(fd.document_id)).ExecuteDeleteAsync();
            await _context.DOCUMENT_CATEGORIES.Where(dc => ids.Contains(dc.document_id)).ExecuteDeleteAsync();
            await _context.DOCUMENT_TAGS.Where(dt => ids.Contains(dt.document_id)).ExecuteDeleteAsync();
            await _context.COLLECTION_DOCUMENTS.Where(cd => ids.Contains(cd.document_id)).ExecuteDeleteAsync();
            await _context.LIKES.Where(l => ids.Contains(l.document_id)).ExecuteDeleteAsync();
            await _context.REPORTS.Where(r => ids.Contains(r.document_id)).ExecuteDeleteAsync();
        }
        //Loại bỏ dấu tiếng việt
        public static string RemoveDiacritics(string text)
        {
            string normalized = text.Normalize(NormalizationForm.FormD);
            Regex regex = new Regex(@"\p{M}");
            string result = regex.Replace(normalized, "").Normalize(NormalizationForm.FormC);
            return result.ToLower().Replace(" ", ""); // Loại bỏ khoảng trắng
        }

        private async Task NotifyFollowersAboutPublishedDocument(Documents document)
        {
            var followerIds = await _context.FOLLOWS
                .AsNoTracking()
                .Where(f => f.following_id == document.user_id)
                .Select(f => f.follower_id)
                .ToListAsync();

            var notifications = followerIds.Select(followerId => new NotificationCreateRequest
            {
                recipientUserId = followerId,
                actorUserId = document.user_id,
                type = "DOCUMENT_PUBLISHED",
                title = "Tài liệu mới từ người bạn theo dõi",
                message = $"Tài liệu \"{document.Title}\" vừa được công khai.",
                relatedDocumentId = document.document_id,
                targetUrl = $"/documents/{document.document_id}"
            });

            await _notificationService.CreateManyAsync(notifications);
        }

        private static bool IsDownloadMilestone(int downloadCount)
        {
            return downloadCount is 10 or 50 or 100 || (downloadCount > 0 && downloadCount % 500 == 0);
        }
    }

}
