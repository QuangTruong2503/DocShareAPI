using CloudinaryDotNet.Actions;
using CloudinaryDotNet;
using DocShareAPI.Data;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers
{
    [ApiController]
    public class DocumentVersionsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly IFolderPermissionService _folderPermissionService;
        private readonly IAuditLogService _auditLogService;
        private readonly long _maxFileSize;
        private readonly string[] _allowedDocumentTypes;

        public DocumentVersionsController(
            DocShareDbContext context,
            ICloudinaryService cloudinaryService,
            IFolderPermissionService folderPermissionService,
            IAuditLogService auditLogService,
            IConfiguration configuration)
        {
            _context = context;
            _cloudinaryService = cloudinaryService;
            _folderPermissionService = folderPermissionService;
            _auditLogService = auditLogService;
            _maxFileSize = configuration.GetValue<long>("MaxFileSize", 10 * 1024 * 1024);
            _allowedDocumentTypes = configuration.GetSection("AllowedDocumentTypes")
                .Get<string[]>() ?? new[]
                {
                    "application/pdf",
                    "application/msword",
                    "text/plain",
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                };
        }

        [HttpGet("/api/documents/{documentId:int}/versions")]
        public async Task<IActionResult> GetVersions(int documentId)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var document = await _context.DOCUMENTS.AsNoTracking().FirstOrDefaultAsync(d => d.document_id == documentId && d.deleted_at == null);
            if (document == null)
                return NotFound(Error("DOCUMENT_NOT_FOUND", "Không tìm thấy tài liệu."));
            if (!await CanViewDocument(document, decodedToken))
                return Forbid();

            var versions = await _context.DOCUMENT_VERSIONS
                .AsNoTracking()
                .Include(v => v.UploadedByUser)
                .Where(v => v.document_id == documentId)
                .OrderByDescending(v => v.version_number)
                .Select(v => new
                {
                    id = v.version_id,
                    documentId = v.document_id,
                    versionNumber = v.version_number,
                    fileUrl = v.file_url,
                    fileSize = v.file_size,
                    v.pages,
                    changeNote = v.change_note,
                    createdAt = v.created_at,
                    uploader = v.UploadedByUser == null ? null : new
                    {
                        userId = v.UploadedByUser.user_id,
                        username = v.UploadedByUser.Username,
                        fullName = v.UploadedByUser.full_name,
                        avatarUrl = v.UploadedByUser.avatar_url
                    },
                    version_id = v.version_id,
                    document_id = v.document_id,
                    version_number = v.version_number,
                    file_url = v.file_url,
                    file_size = v.file_size,
                    pages_count = v.pages,
                    change_note = v.change_note,
                    created_at = v.created_at,
                    uploadedBy = v.UploadedByUser == null ? null : new
                    {
                        v.UploadedByUser.user_id,
                        v.UploadedByUser.Username,
                        v.UploadedByUser.full_name,
                        v.UploadedByUser.avatar_url
                    }
                })
                .ToListAsync();

            return Ok(new { success = true, currentVersion = versions.FirstOrDefault()?.version_number ?? 1, versions });
        }

        [HttpPost("/api/documents/{documentId:int}/versions")]
        public async Task<IActionResult> UploadNewVersion(int documentId, IFormFile file, [FromForm] string? changeNote = null)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            if (file == null || file.Length == 0)
                return BadRequest(Error("FILE_REQUIRED", "Vui lòng chọn tài liệu để tải lên."));
            if (!IsValidDocument(file, out var validationMessage))
                return BadRequest(Error("INVALID_FILE", validationMessage));

            var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentId && d.deleted_at == null);
            if (document == null)
                return NotFound(Error("DOCUMENT_NOT_FOUND", "Không tìm thấy tài liệu."));
            if (!await CanEditDocument(document, decodedToken))
                return Forbid();

            var quotaError = await ValidateStorageQuota(document.user_id, file.Length);
            if (quotaError != null)
                return quotaError;

            var uploadResult = await UploadToCloudinary(file);
            if (uploadResult == null || uploadResult.Error != null)
                return StatusCode(500, Error("UPLOAD_FAILED", uploadResult?.Error?.Message ?? "Không thể tải phiên bản mới."));

            var maxVersion = await _context.DOCUMENT_VERSIONS
                .Where(v => v.document_id == documentId)
                .MaxAsync(v => (int?)v.version_number) ?? 0;

            if (maxVersion == 0)
            {
                _context.DOCUMENT_VERSIONS.Add(new DocumentVersions
                {
                    document_id = documentId,
                    version_number = 1,
                    file_url = document.file_url,
                    public_id = document.public_id,
                    asset_id = document.asset_id,
                    file_size = document.file_size,
                    pages = document.pages,
                    uploaded_by = document.user_id,
                    change_note = "Phiên bản ban đầu",
                    created_at = document.uploaded_at
                });
                maxVersion = 1;
            }

            var newVersion = new DocumentVersions
            {
                document_id = documentId,
                version_number = maxVersion + 1,
                file_url = uploadResult.SecureUrl.ToString(),
                public_id = uploadResult.PublicId,
                asset_id = uploadResult.AssetId,
                file_size = Convert.ToInt32(file.Length),
                pages = uploadResult.Pages,
                uploaded_by = decodedToken.userID,
                change_note = changeNote,
                created_at = DateTime.UtcNow
            };

            _context.DOCUMENT_VERSIONS.Add(newVersion);

            document.file_url = newVersion.file_url;
            document.public_id = newVersion.public_id;
            document.asset_id = newVersion.asset_id;
            document.file_size = newVersion.file_size;
            document.file_type = uploadResult.Format;
            document.pages = newVersion.pages;
            document.thumbnail_url = Helpers.ConvertPdf.ConvertPdfTitleToJpg(newVersion.file_url);

            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(decodedToken.userID, "document_version.created", "document", documentId.ToString(), new { newVersion.version_number }, HttpContext.Connection.RemoteIpAddress?.ToString());

            return Ok(new
            {
                success = true,
                version = new
                {
                    id = newVersion.version_id,
                    documentId = newVersion.document_id,
                    versionNumber = newVersion.version_number,
                    fileUrl = newVersion.file_url,
                    fileSize = newVersion.file_size,
                    changeNote = newVersion.change_note,
                    createdAt = newVersion.created_at,
                    newVersion.version_id,
                    newVersion.document_id,
                    newVersion.version_number,
                    newVersion.file_url,
                    newVersion.file_size,
                    newVersion.pages,
                    newVersion.change_note,
                    newVersion.created_at
                }
            });
        }

        [HttpPost("/api/documents/{documentId:int}/versions/{versionId:int}/restore")]
        public async Task<IActionResult> RestoreVersion(int documentId, int versionId)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentId && d.deleted_at == null);
            if (document == null)
                return NotFound(Error("DOCUMENT_NOT_FOUND", "Không tìm thấy tài liệu."));
            if (!await CanEditDocument(document, decodedToken))
                return Forbid();

            var version = await _context.DOCUMENT_VERSIONS.FirstOrDefaultAsync(v => v.version_id == versionId && v.document_id == documentId);
            if (version == null)
                return NotFound(Error("VERSION_NOT_FOUND", "Không tìm thấy phiên bản."));

            var maxVersion = await _context.DOCUMENT_VERSIONS
                .Where(v => v.document_id == documentId)
                .MaxAsync(v => (int?)v.version_number) ?? 0;

            var restoredVersion = new DocumentVersions
            {
                document_id = documentId,
                version_number = maxVersion + 1,
                file_url = version.file_url,
                public_id = version.public_id,
                asset_id = version.asset_id,
                file_size = version.file_size,
                pages = version.pages,
                uploaded_by = decodedToken.userID,
                change_note = $"Khôi phục từ phiên bản {version.version_number}",
                created_at = DateTime.UtcNow
            };

            _context.DOCUMENT_VERSIONS.Add(restoredVersion);
            document.file_url = version.file_url;
            document.public_id = version.public_id;
            document.asset_id = version.asset_id;
            document.file_size = version.file_size;
            document.pages = version.pages;
            document.thumbnail_url = Helpers.ConvertPdf.ConvertPdfTitleToJpg(version.file_url);

            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(decodedToken.userID, "document_version.restored", "document", documentId.ToString(), new { versionId, restoredVersion.version_number }, HttpContext.Connection.RemoteIpAddress?.ToString());

            return Ok(new { success = true, restoredVersionId = restoredVersion.version_id, restoredVersionNumber = restoredVersion.version_number });
        }

        private async Task<bool> CanViewDocument(Documents document, DecodedTokenResponse decodedToken)
        {
            if (document.is_public || document.user_id == decodedToken.userID || decodedToken.roleID == "admin")
                return true;

            var folderId = await _context.FOLDER_DOCUMENTS
                .AsNoTracking()
                .Where(fd => fd.document_id == document.document_id)
                .Select(fd => (int?)fd.folder_id)
                .FirstOrDefaultAsync();

            return folderId.HasValue && await _folderPermissionService.CanViewFolderAsync(decodedToken.userID, folderId.Value);
        }

        private async Task<bool> CanEditDocument(Documents document, DecodedTokenResponse decodedToken)
        {
            if (document.user_id == decodedToken.userID || decodedToken.roleID == "admin")
                return true;

            var folderId = await _context.FOLDER_DOCUMENTS
                .AsNoTracking()
                .Where(fd => fd.document_id == document.document_id)
                .Select(fd => (int?)fd.folder_id)
                .FirstOrDefaultAsync();

            return folderId.HasValue && await _folderPermissionService.CanAddDocumentToFolderAsync(decodedToken.userID, folderId.Value);
        }

        private bool IsValidDocument(IFormFile file, out string validationMessage)
        {
            validationMessage = string.Empty;
            if (file.Length > _maxFileSize)
            {
                validationMessage = $"Kích thước file vượt quá giới hạn cho phép ({_maxFileSize / 1024 / 1024}MB).";
                return false;
            }

            if (!_allowedDocumentTypes.Contains(file.ContentType))
            {
                validationMessage = "Loại file không được hỗ trợ.";
                return false;
            }

            return true;
        }

        private async Task<IActionResult?> ValidateStorageQuota(Guid ownerUserId, long incomingBytes)
        {
            const long defaultStorageLimitBytes = 10L * 1024 * 1024 * 1024;
            var limitBytes = await _context.USERS
                .AsNoTracking()
                .Where(u => u.user_id == ownerUserId)
                .Select(u => u.storage_limit_bytes)
                .FirstOrDefaultAsync() ?? defaultStorageLimitBytes;

            var usedBytes = await _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.user_id == ownerUserId && d.deleted_at == null)
                .SumAsync(d => (long)d.file_size);

            if (usedBytes + incomingBytes <= limitBytes)
                return null;

            return StatusCode(StatusCodes.Status413PayloadTooLarge, Error("STORAGE_QUOTA_EXCEEDED", "Dung lượng lưu trữ không đủ để tải lên phiên bản mới.", new
            {
                usedBytes,
                incomingBytes,
                limitBytes,
                remainingBytes = Math.Max(0, limitBytes - usedBytes)
            }));
        }

        private async Task<ImageUploadResult> UploadToCloudinary(IFormFile file)
        {
            using var stream = file.OpenReadStream();
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = "DocShare/Documents",
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = false,
                Tags = file.ContentType
            };

            return await _cloudinaryService.Cloudinary.UploadAsync(uploadParams);
        }

        private static object Error(string code, string message, object? details = null) => new { success = false, code, message, details };
    }
}
