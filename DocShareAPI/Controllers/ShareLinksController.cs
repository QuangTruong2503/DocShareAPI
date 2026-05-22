using DocShareAPI.Data;
using DocShareAPI.Models;
using DocShareAPI.Services;
using ELearningAPI.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace DocShareAPI.Controllers
{
    [ApiController]
    public class ShareLinksController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IAuditLogService _auditLogService;

        public ShareLinksController(
            DocShareDbContext context,
            IConfiguration configuration,
            IAuditLogService auditLogService)
        {
            _context = context;
            _configuration = configuration;
            _auditLogService = auditLogService;
        }

        [HttpPost("/api/share-links")]
        public async Task<IActionResult> CreateOrUpdateShareLink([FromBody] ShareLinkRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var itemType = NormalizeType(request.itemType);
            if (itemType == null || request.itemId <= 0)
                return BadRequest(Error("VALIDATION_ERROR", "itemId và itemType hợp lệ là bắt buộc."));

            if (!await CanShare(decodedToken, request.itemId, itemType))
                return Forbid();

            var link = await _context.SHARE_LINKS
                .FirstOrDefaultAsync(s =>
                    s.owner_user_id == decodedToken.userID &&
                    s.item_id == request.itemId &&
                    s.item_type == itemType &&
                    s.revoked_at == null);

            var isNew = link == null;
            link ??= new ShareLinks
            {
                token = GenerateToken(),
                owner_user_id = decodedToken.userID,
                item_id = request.itemId,
                item_type = itemType,
                created_at = DateTime.UtcNow
            };

            link.access = string.IsNullOrWhiteSpace(request.access) ? "anyone_with_link" : request.access!.Trim();
            link.permission = NormalizePermission(request.permission);
            link.allow_download = request.allowDownload;
            link.expires_at = request.expiresAt;
            link.max_views = request.maxViews;
            link.max_downloads = request.maxDownloads;
            link.updated_at = DateTime.UtcNow;

            if (request.password != null)
                link.password_hash = string.IsNullOrWhiteSpace(request.password) ? null : PasswordHasher.HashPassword(request.password);

            if (isNew)
                _context.SHARE_LINKS.Add(link);

            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(decodedToken.userID, isNew ? "share_link.created" : "share_link.updated", itemType, request.itemId.ToString(), new { link.token }, HttpContext.Connection.RemoteIpAddress?.ToString());

            return Ok(new { success = true, shareLink = await ToResponse(link) });
        }

        [HttpGet("/api/share-links")]
        public async Task<IActionResult> GetShareLink([FromQuery] int itemId, [FromQuery] string? itemType)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var normalizedType = NormalizeType(itemType);
            if (normalizedType == null || itemId <= 0)
                return BadRequest(Error("VALIDATION_ERROR", "itemId và itemType hợp lệ là bắt buộc."));

            var link = await _context.SHARE_LINKS
                .AsNoTracking()
                .FirstOrDefaultAsync(s =>
                    s.owner_user_id == decodedToken.userID &&
                    s.item_id == itemId &&
                    s.item_type == normalizedType &&
                    s.revoked_at == null);

            return Ok(new { success = true, shareLink = link == null ? null : await ToResponse(link) });
        }

        [HttpDelete("/api/share-links/{shareLinkId}")]
        public async Task<IActionResult> DeleteShareLink(string shareLinkId)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var link = await _context.SHARE_LINKS
                .FirstOrDefaultAsync(s => s.token == shareLinkId && s.owner_user_id == decodedToken.userID && s.revoked_at == null);

            if (link == null)
                return NotFound(Error("SHARE_LINK_NOT_FOUND", "Không tìm thấy share link."));

            link.revoked_at = DateTime.UtcNow;
            link.updated_at = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(decodedToken.userID, "share_link.revoked", link.item_type, link.item_id.ToString(), new { link.token }, HttpContext.Connection.RemoteIpAddress?.ToString());

            return Ok(new { success = true, deletedId = shareLinkId });
        }

        [HttpGet("/api/share-links/my")]
        public async Task<IActionResult> GetMyShareLinks([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 50)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var page = Math.Max(pageNumber, 1);
            var size = Math.Clamp(pageSize, 1, 100);
            var query = _context.SHARE_LINKS
                .AsNoTracking()
                .Where(s => s.owner_user_id == decodedToken.userID && s.revoked_at == null)
                .OrderByDescending(s => s.created_at);

            var totalCount = await query.CountAsync();
            var links = await query
                .Skip((page - 1) * size)
                .Take(size)
                .ToListAsync();

            var responses = new List<object>();
            foreach (var link in links)
                responses.Add(await ToResponse(link));

            return Ok(new
            {
                success = true,
                shareLinks = responses,
                pagination = new
                {
                    currentPage = page,
                    pageSize = size,
                    totalCount,
                    totalPages = (int)Math.Ceiling(totalCount / (double)size)
                }
            });
        }

        [AllowAnonymous]
        [HttpGet("/api/s/{shareToken}")]
        public async Task<IActionResult> GetPublicShare(string shareToken)
        {
            var link = await GetActivePublicLink(shareToken);
            if (link == null)
                return NotFound(Error("SHARE_LINK_NOT_FOUND", "Không tìm thấy share link."));

            var availabilityError = ValidateAvailability(link);
            if (availabilityError != null)
                return availabilityError;

            if (!string.IsNullOrEmpty(link.password_hash))
                return Ok(new { success = true, item = (object?)null, permission = link.permission, allowDownload = link.allow_download, requiresPassword = true });

            link.view_count++;
            link.updated_at = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var item = await BuildShareItem(link);
            return Ok(new { success = true, item, permission = link.permission, allowDownload = link.allow_download, requiresPassword = false });
        }

        [AllowAnonymous]
        [HttpPost("/api/s/{shareToken}/verify-password")]
        public async Task<IActionResult> VerifyPassword(string shareToken, [FromBody] SharePasswordRequest request)
        {
            var link = await GetActivePublicLink(shareToken);
            if (link == null)
                return NotFound(Error("SHARE_LINK_NOT_FOUND", "Không tìm thấy share link."));

            var availabilityError = ValidateAvailability(link);
            if (availabilityError != null)
                return availabilityError;

            if (string.IsNullOrEmpty(link.password_hash) ||
                string.IsNullOrWhiteSpace(request.password) ||
                !PasswordHasher.VerifyPassword(request.password, link.password_hash))
            {
                return Forbid();
            }

            link.view_count++;
            link.updated_at = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var item = await BuildShareItem(link);
            return Ok(new
            {
                success = true,
                item,
                permission = link.permission,
                allowDownload = link.allow_download,
                requiresPassword = false
            });
        }

        private async Task<ShareLinks?> GetActivePublicLink(string shareToken)
        {
            return await _context.SHARE_LINKS
                .FirstOrDefaultAsync(s => s.token == shareToken && s.revoked_at == null);
        }

        private IActionResult? ValidateAvailability(ShareLinks link)
        {
            if (link.expires_at.HasValue && link.expires_at.Value <= DateTime.UtcNow)
                return StatusCode(StatusCodes.Status410Gone, Error("SHARE_LINK_EXPIRED", "Share link đã hết hạn."));

            if (link.max_views.HasValue && link.view_count >= link.max_views.Value)
                return StatusCode(StatusCodes.Status410Gone, Error("SHARE_LINK_VIEW_LIMIT_REACHED", "Share link đã đạt giới hạn lượt xem."));

            if (link.max_downloads.HasValue && link.download_count >= link.max_downloads.Value)
                return StatusCode(StatusCodes.Status410Gone, Error("SHARE_LINK_DOWNLOAD_LIMIT_REACHED", "Share link đã đạt giới hạn lượt tải."));

            return null;
        }

        private async Task<bool> CanShare(DecodedTokenResponse decodedToken, int itemId, string itemType)
        {
            if (itemType == "document")
                return await _context.DOCUMENTS.AnyAsync(d => d.document_id == itemId && d.deleted_at == null && (d.user_id == decodedToken.userID || decodedToken.roleID == "admin"));

            return await _context.FOLDERS.AnyAsync(f => f.folder_id == itemId && f.deleted_at == null && f.owner_user_id == decodedToken.userID);
        }

        private async Task<object?> BuildShareItem(ShareLinks link)
        {
            if (link.item_type == "document")
            {
                var document = await _context.DOCUMENTS
                    .AsNoTracking()
                    .Include(d => d.Users)
                    .FirstOrDefaultAsync(d => d.document_id == link.item_id && d.deleted_at == null);

                return document == null ? null : LibraryController.ToDocumentItem(document, null, link.permission);
            }

            var folder = await _context.FOLDERS
                .AsNoTracking()
                .Include(f => f.OwnerUser)
                .FirstOrDefaultAsync(f => f.folder_id == link.item_id && f.deleted_at == null);

            return folder == null ? null : LibraryController.ToFolderItem(folder, link.permission, isShared: true);
        }

        private async Task<object> ToResponse(ShareLinks link)
        {
            var baseUrl = _configuration["PublicBaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
            return new
            {
                id = link.token,
                itemId = link.item_id,
                itemType = link.item_type,
                itemName = await GetItemName(link),
                shareUrl = $"{baseUrl}/s/{link.token}",
                access = link.access,
                permission = link.permission,
                allowDownload = link.allow_download,
                requiresPassword = !string.IsNullOrEmpty(link.password_hash),
                views = link.view_count,
                downloads = link.download_count,
                maxViews = link.max_views,
                maxDownloads = link.max_downloads,
                expiresAt = link.expires_at,
                createdAt = link.created_at,
                updatedAt = link.updated_at
            };
        }

        private async Task<string?> GetItemName(ShareLinks link)
        {
            if (link.item_type == "document")
                return await _context.DOCUMENTS.AsNoTracking().Where(d => d.document_id == link.item_id).Select(d => d.Title).FirstOrDefaultAsync();

            return await _context.FOLDERS.AsNoTracking().Where(f => f.folder_id == link.item_id).Select(f => f.name).FirstOrDefaultAsync();
        }

        private static string? NormalizeType(string? type)
        {
            var normalized = type?.Trim().ToLowerInvariant();
            return normalized is "document" or "folder" ? normalized : null;
        }

        private static string NormalizePermission(string? permission)
        {
            var normalized = permission?.Trim().ToLowerInvariant();
            return normalized is "viewer" or "editor" ? normalized : "viewer";
        }

        private static string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        private static object Error(string code, string message, object? details = null) => new { success = false, code, message, details };
    }

    public class ShareLinkRequest
    {
        public int itemId { get; set; }
        public string? itemType { get; set; }
        public string? access { get; set; }
        public string? permission { get; set; }
        public bool allowDownload { get; set; } = true;
        public string? password { get; set; }
        public DateTime? expiresAt { get; set; }
        public int? maxViews { get; set; }
        public int? maxDownloads { get; set; }
    }

    public class SharePasswordRequest
    {
        public string? password { get; set; }
    }
}
