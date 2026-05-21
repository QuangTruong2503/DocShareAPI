using DocShareAPI.Data;
using DocShareAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace DocShareAPI.Controllers
{
    [ApiController]
    public class ShareLinksController : ControllerBase
    {
        private static readonly Dictionary<string, ShareLinkState> ShareLinks = new();
        private readonly DocShareDbContext _context;
        private readonly IConfiguration _configuration;

        public ShareLinksController(DocShareDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("/api/share-links")]
        public async Task<IActionResult> CreateOrUpdateShareLink([FromBody] ShareLinkRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            if (!IsValidType(request.itemType) || request.itemId <= 0)
                return BadRequest(Error("VALIDATION_ERROR", "itemId và itemType hợp lệ là bắt buộc."));

            if (!await CanShare(decodedToken, request.itemId, request.itemType!))
                return Forbid();

            var existing = ShareLinks.Values.FirstOrDefault(s => s.ItemId == request.itemId && s.ItemType == request.itemType);
            var link = existing ?? new ShareLinkState { Id = GenerateToken(), ItemId = request.itemId, ItemType = request.itemType!, CreatedAt = DateTime.UtcNow };
            link.Access = string.IsNullOrWhiteSpace(request.access) ? "anyone_with_link" : request.access!;
            link.Permission = string.IsNullOrWhiteSpace(request.permission) ? "viewer" : request.permission!;
            link.AllowDownload = request.allowDownload;
            link.ExpiresAt = request.expiresAt;
            link.OwnerId = decodedToken.userID;
            link.Password = request.password;

            ShareLinks[link.Id] = link;

            return Ok(new { shareLink = ToResponse(link) });
        }

        [HttpGet("/api/share-links")]
        public IActionResult GetShareLink([FromQuery] int itemId, [FromQuery] string? itemType)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var link = ShareLinks.Values.FirstOrDefault(s => s.ItemId == itemId && s.ItemType == itemType);
            return Ok(new { shareLink = link == null ? null : ToResponse(link) });
        }

        [HttpDelete("/api/share-links/{shareLinkId}")]
        public IActionResult DeleteShareLink(string shareLinkId)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            ShareLinks.Remove(shareLinkId);
            return Ok(new { success = true, deletedId = shareLinkId });
        }

        [HttpGet("/api/share-links/my")]
        public IActionResult GetMyShareLinks([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 50)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var links = ShareLinks.Values
                .Where(s => s.OwnerId == decodedToken.userID)
                .OrderByDescending(s => s.CreatedAt)
                .Skip((Math.Max(pageNumber, 1) - 1) * pageSize)
                .Take(pageSize)
                .Select(ToResponse)
                .ToList();

            return Ok(new
            {
                shareLinks = links,
                pagination = new { currentPage = Math.Max(pageNumber, 1), pageSize, totalCount = links.Count, totalPages = 1 }
            });
        }

        [HttpGet("/api/s/{shareToken}")]
        public async Task<IActionResult> GetPublicShare(string shareToken)
        {
            if (!ShareLinks.TryGetValue(shareToken, out var link))
                return NotFound(Error("SHARE_LINK_NOT_FOUND", "Không tìm thấy share link."));
            if (link.ExpiresAt.HasValue && link.ExpiresAt.Value <= DateTime.UtcNow)
                return StatusCode(StatusCodes.Status410Gone, Error("SHARE_LINK_EXPIRED", "Share link đã hết hạn."));
            if (!string.IsNullOrEmpty(link.Password))
                return Ok(new { item = (object?)null, permission = link.Permission, allowDownload = link.AllowDownload, requiresPassword = true });

            var item = await BuildShareItem(link);
            return Ok(new { item, permission = link.Permission, allowDownload = link.AllowDownload, requiresPassword = false });
        }

        [HttpPost("/api/s/{shareToken}/verify-password")]
        public IActionResult VerifyPassword(string shareToken, [FromBody] SharePasswordRequest request)
        {
            if (!ShareLinks.TryGetValue(shareToken, out var link))
                return NotFound(Error("SHARE_LINK_NOT_FOUND", "Không tìm thấy share link."));
            if (link.Password != request.password)
                return Forbid();

            return Ok(new { accessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
        }

        private async Task<bool> CanShare(DecodedTokenResponse decodedToken, int itemId, string itemType)
        {
            if (itemType == "document")
                return await _context.DOCUMENTS.AnyAsync(d => d.document_id == itemId && d.deleted_at == null && (d.user_id == decodedToken.userID || decodedToken.roleID == "admin"));
            return await _context.FOLDERS.AnyAsync(f => f.folder_id == itemId && f.deleted_at == null && f.owner_user_id == decodedToken.userID);
        }

        private async Task<object?> BuildShareItem(ShareLinkState link)
        {
            if (link.ItemType == "document")
            {
                var document = await _context.DOCUMENTS.AsNoTracking().Include(d => d.Users).FirstOrDefaultAsync(d => d.document_id == link.ItemId && d.deleted_at == null);
                return document == null ? null : LibraryController.ToDocumentItem(document, null, link.Permission);
            }

            var folder = await _context.FOLDERS.AsNoTracking().Include(f => f.OwnerUser).FirstOrDefaultAsync(f => f.folder_id == link.ItemId && f.deleted_at == null);
            return folder == null ? null : LibraryController.ToFolderItem(folder, link.Permission, isShared: true);
        }

        private object ToResponse(ShareLinkState link)
        {
            var baseUrl = _configuration["PublicBaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
            return new
            {
                id = link.Id,
                itemId = link.ItemId,
                itemType = link.ItemType,
                itemName = (string?)null,
                shareUrl = $"{baseUrl}/s/{link.Id}",
                access = link.Access,
                permission = link.Permission,
                allowDownload = link.AllowDownload,
                views = 0,
                downloads = 0,
                expiresAt = link.ExpiresAt,
                createdAt = link.CreatedAt
            };
        }

        private static bool IsValidType(string? type) => type is "document" or "folder";
        private static string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)).Replace("+", "-").Replace("/", "_").TrimEnd('=');
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

    public class ShareLinkState
    {
        public required string Id { get; set; }
        public int ItemId { get; set; }
        public required string ItemType { get; set; }
        public Guid OwnerId { get; set; }
        public string Access { get; set; } = "anyone_with_link";
        public string Permission { get; set; } = "viewer";
        public bool AllowDownload { get; set; } = true;
        public string? Password { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
