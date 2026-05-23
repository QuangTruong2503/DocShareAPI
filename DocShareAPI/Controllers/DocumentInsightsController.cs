using DocShareAPI.Data;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace DocShareAPI.Controllers
{
    [ApiController]
    public class DocumentInsightsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly IFolderPermissionService _folderPermissionService;
        private readonly IAuditLogService _auditLogService;

        public DocumentInsightsController(
            DocShareDbContext context,
            IFolderPermissionService folderPermissionService,
            IAuditLogService auditLogService)
        {
            _context = context;
            _folderPermissionService = folderPermissionService;
            _auditLogService = auditLogService;
        }

        [AllowAnonymous]
        [HttpPost("/api/documents/{documentId:int}/view")]
        public async Task<IActionResult> RecordView(int documentId, [FromBody] RecordViewRequest? request)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            var document = await _context.DOCUMENTS.AsNoTracking().FirstOrDefaultAsync(d => d.document_id == documentId && d.deleted_at == null);
            if (document == null)
                return NotFound(Error("DOCUMENT_NOT_FOUND", "Không tìm thấy tài liệu."));

            if (!await CanViewDocument(document, decodedToken))
                return decodedToken == null ? Unauthorized(Error("UNAUTHORIZED", "Bạn cần đăng nhập để xem tài liệu này.")) : Forbid();

            var ipHash = HashValue(HttpContext.Connection.RemoteIpAddress?.ToString());
            var now = DateTime.UtcNow;
            var dedupeTime = now.AddMinutes(-30);

            var exists = await _context.DOCUMENT_VIEWS.AnyAsync(v =>
                v.document_id == documentId &&
                v.viewed_at >= dedupeTime &&
                ((decodedToken != null && v.user_id == decodedToken.userID) ||
                 (decodedToken == null && v.ip_hash == ipHash)));

            if (!exists)
            {
                _context.DOCUMENT_VIEWS.Add(new DocumentViews
                {
                    document_id = documentId,
                    user_id = decodedToken?.userID,
                    ip_hash = ipHash,
                    source = string.IsNullOrWhiteSpace(request?.source) ? "document_detail" : request!.source!.Trim(),
                    viewed_at = now
                });
                await _context.SaveChangesAsync();
            }

            var totalViews = await _context.DOCUMENT_VIEWS.AsNoTracking().CountAsync(v => v.document_id == documentId);
            return Ok(new { success = true, recorded = !exists, documentId, totalViews });
        }

        [HttpGet("/api/users/me/history")]
        public async Task<IActionResult> GetMyViewHistory([FromQuery] PaginationParams paginationParams)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var page = paginationParams.PageNumber <= 0 ? 1 : paginationParams.PageNumber;
            var pageSize = paginationParams.PageSize <= 0 ? 20 : paginationParams.PageSize;

            var latestViews = _context.DOCUMENT_VIEWS
                .AsNoTracking()
                .Where(v => v.user_id == decodedToken.userID)
                .GroupBy(v => v.document_id)
                .Select(g => new { document_id = g.Key, viewed_at = g.Max(v => v.viewed_at) });

            var query = latestViews
                .Join(_context.DOCUMENTS.AsNoTracking().Include(d => d.Users),
                    view => view.document_id,
                    document => document.document_id,
                    (view, document) => new { view, document })
                .Where(x => x.document.deleted_at == null)
                .OrderByDescending(x => x.view.viewed_at);

            var totalCount = await query.CountAsync();
            var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new
            {
                success = true,
                items = rows.Select(x => new
                {
                    viewedAt = x.view.viewed_at,
                    document = LibraryController.ToDocumentItem(x.document, null, x.document.user_id == decodedToken.userID ? "owner" : "viewer")
                }),
                pagination = Pagination(page, pageSize, totalCount)
            });
        }

        [AllowAnonymous]
        [HttpGet("/api/documents/trending")]
        public async Task<IActionResult> GetTrendingDocuments([FromQuery] PaginationParams paginationParams, [FromQuery] int days = 7)
        {
            var page = paginationParams.PageNumber <= 0 ? 1 : paginationParams.PageNumber;
            var pageSize = paginationParams.PageSize <= 0 ? 20 : paginationParams.PageSize;
            var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));

            var scores = _context.DOCUMENT_VIEWS
                .AsNoTracking()
                .Where(v => v.viewed_at >= since)
                .GroupBy(v => v.document_id)
                .Select(g => new { document_id = g.Key, views = g.Count() });

            var query = scores
                .Join(_context.DOCUMENTS.AsNoTracking().Include(d => d.Users).Where(d => d.is_public && d.deleted_at == null),
                    score => score.document_id,
                    document => document.document_id,
                    (score, document) => new { score, document })
                .OrderByDescending(x => x.score.views)
                .ThenByDescending(x => x.document.download_count);

            var totalCount = await query.CountAsync();
            var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new
            {
                success = true,
                days = Math.Clamp(days, 1, 365),
                items = rows.Select(x => new
                {
                    views = x.score.views,
                    downloads = x.document.download_count,
                    document = LibraryController.ToDocumentItem(x.document, null, "viewer")
                }),
                pagination = Pagination(page, pageSize, totalCount)
            });
        }

        [HttpGet("/api/feed/recommended")]
        public async Task<IActionResult> GetRecommendedDocuments([FromQuery] PaginationParams paginationParams)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var page = paginationParams.PageNumber <= 0 ? 1 : paginationParams.PageNumber;
            var pageSize = paginationParams.PageSize <= 0 ? 20 : paginationParams.PageSize;

            var viewedDocumentIds = await _context.DOCUMENT_VIEWS
                .AsNoTracking()
                .Where(v => v.user_id == decodedToken.userID)
                .Select(v => v.document_id)
                .Distinct()
                .ToListAsync();

            var categoryIds = await _context.DOCUMENT_CATEGORIES
                .AsNoTracking()
                .Where(dc => viewedDocumentIds.Contains(dc.document_id))
                .GroupBy(dc => dc.category_id)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(10)
                .ToListAsync();

            var query = _context.DOCUMENTS
                .AsNoTracking()
                .Include(d => d.Users)
                .Where(d => d.is_public && d.deleted_at == null && d.user_id != decodedToken.userID && !viewedDocumentIds.Contains(d.document_id));

            if (categoryIds.Count > 0)
            {
                query = query.Where(d => d.DocumentCategories.Any(dc => categoryIds.Contains(dc.category_id)));
            }

            query = query
                .OrderByDescending(d => d.download_count)
                .ThenByDescending(d => d.uploaded_at);

            var totalCount = await query.CountAsync();
            var documents = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new
            {
                success = true,
                items = documents.Select(d => LibraryController.ToDocumentItem(d, null, "viewer")),
                pagination = Pagination(page, pageSize, totalCount)
            });
        }

        [HttpGet("/api/feed/following")]
        public async Task<IActionResult> GetFollowingFeed([FromQuery] PaginationParams paginationParams)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var page = paginationParams.PageNumber <= 0 ? 1 : paginationParams.PageNumber;
            var pageSize = paginationParams.PageSize <= 0 ? 20 : paginationParams.PageSize;

            var followingIds = _context.FOLLOWS
                .AsNoTracking()
                .Where(f => f.follower_id == decodedToken.userID)
                .Select(f => f.following_id);

            var query = _context.DOCUMENTS
                .AsNoTracking()
                .Include(d => d.Users)
                .Where(d => d.is_public && d.deleted_at == null && followingIds.Contains(d.user_id))
                .OrderByDescending(d => d.uploaded_at);

            var totalCount = await query.CountAsync();
            var documents = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new
            {
                success = true,
                items = documents.Select(d => LibraryController.ToDocumentItem(d, null, "viewer")),
                pagination = Pagination(page, pageSize, totalCount)
            });
        }

        [HttpGet("/api/admin/analytics/engagement")]
        public async Task<IActionResult> GetEngagementAnalytics([FromQuery] int days = 30)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));
            if (decodedToken.roleID != "admin")
                return Forbid();

            var normalizedDays = Math.Clamp(days, 1, 365);
            var since = DateTime.UtcNow.AddDays(-normalizedDays);

            var dailyViewRows = await _context.DOCUMENT_VIEWS
                .AsNoTracking()
                .Where(v => v.viewed_at >= since)
                .GroupBy(v => v.viewed_at.Date)
                .Select(g => new { date = g.Key, count = g.Count() })
                .OrderBy(x => x.date)
                .ToListAsync();

            var dailyDownloadRows = await _context.DOCUMENT_DOWNLOADS
                .AsNoTracking()
                .Where(d => d.downloaded_at >= since)
                .GroupBy(d => d.downloaded_at.Date)
                .Select(g => new { date = g.Key, count = g.Count() })
                .OrderBy(x => x.date)
                .ToListAsync();

            var dailyViewCounts = dailyViewRows.ToDictionary(x => DateOnly.FromDateTime(x.date), x => x.count);
            var dailyDownloadCounts = dailyDownloadRows.ToDictionary(x => DateOnly.FromDateTime(x.date), x => x.count);
            var daysRange = Enumerable.Range(0, normalizedDays)
                .Select(offset => DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-(normalizedDays - 1 - offset))))
                .ToList();
            var dailyViews = daysRange.Select(date => new { date = date.ToString("yyyy-MM-dd"), count = dailyViewCounts.GetValueOrDefault(date) }).ToList();
            var dailyDownloads = daysRange.Select(date => new { date = date.ToString("yyyy-MM-dd"), count = dailyDownloadCounts.GetValueOrDefault(date) }).ToList();
            var totalViews = await _context.DOCUMENT_VIEWS.CountAsync(v => v.viewed_at >= since);
            var totalDownloads = await _context.DOCUMENT_DOWNLOADS.CountAsync(d => d.downloaded_at >= since);

            return Ok(new
            {
                success = true,
                days = normalizedDays,
                totalViews,
                totalDownloads,
                dailyViews,
                dailyDownloads,
                data = new
                {
                    days = normalizedDays,
                    totalViews,
                    totalDownloads,
                    dailyViews,
                    dailyDownloads
                }
            });
        }

        [HttpGet("/api/documents/{documentId:int}/insights")]
        public async Task<IActionResult> GetDocumentInsights(int documentId, [FromQuery] int days = 30)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var document = await _context.DOCUMENTS
                .AsNoTracking()
                .Include(d => d.Users)
                .FirstOrDefaultAsync(d => d.document_id == documentId && d.deleted_at == null);

            if (document == null)
                return NotFound(Error("DOCUMENT_NOT_FOUND", "Không tìm thấy tài liệu."));

            if (!CanManageDocumentInsights(document, decodedToken))
                return Forbid();

            var normalizedDays = Math.Clamp(days, 1, 365);
            var since = DateTime.UtcNow.Date.AddDays(-(normalizedDays - 1));
            var today = DateTime.UtcNow.Date;

            var totalViews = await _context.DOCUMENT_VIEWS.AsNoTracking().CountAsync(v => v.document_id == documentId);
            var totalDownloads = await _context.DOCUMENT_DOWNLOADS.AsNoTracking().CountAsync(d => d.document_id == documentId);
            var totalLikes = await _context.LIKES.AsNoTracking().CountAsync(l => l.document_id == documentId && l.reaction == 1);
            var totalDislikes = await _context.LIKES.AsNoTracking().CountAsync(l => l.document_id == documentId && l.reaction == -1);
            var totalComments = await _context.COMMENTS.AsNoTracking().CountAsync(c => c.document_id == documentId && c.deleted_at == null);
            var totalShares = await _context.SHARE_LINKS.AsNoTracking().CountAsync(s => s.item_type == "document" && s.item_id == documentId);
            var totalVersions = await _context.DOCUMENT_VERSIONS.AsNoTracking().CountAsync(v => v.document_id == documentId);

            var dailyViewRows = await _context.DOCUMENT_VIEWS
                .AsNoTracking()
                .Where(v => v.document_id == documentId && v.viewed_at >= since)
                .GroupBy(v => v.viewed_at.Date)
                .Select(g => new { date = g.Key, count = g.Count() })
                .ToListAsync();

            var dailyDownloadRows = await _context.DOCUMENT_DOWNLOADS
                .AsNoTracking()
                .Where(d => d.document_id == documentId && d.downloaded_at >= since)
                .GroupBy(d => d.downloaded_at.Date)
                .Select(g => new { date = g.Key, count = g.Count() })
                .ToListAsync();

            var viewCounts = dailyViewRows.ToDictionary(x => DateOnly.FromDateTime(x.date), x => x.count);
            var downloadCounts = dailyDownloadRows.ToDictionary(x => DateOnly.FromDateTime(x.date), x => x.count);
            var daily = Enumerable.Range(0, normalizedDays)
                .Select(offset =>
                {
                    var date = DateOnly.FromDateTime(today.AddDays(-(normalizedDays - 1 - offset)));
                    return new
                    {
                        date = date.ToString("yyyy-MM-dd"),
                        views = viewCounts.GetValueOrDefault(date),
                        downloads = downloadCounts.GetValueOrDefault(date)
                    };
                })
                .ToList();

            var sources = await _context.DOCUMENT_VIEWS
                .AsNoTracking()
                .Where(v => v.document_id == documentId && v.viewed_at >= since)
                .GroupBy(v => string.IsNullOrWhiteSpace(v.source) ? "unknown" : v.source!)
                .Select(g => new { source = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(8)
                .ToListAsync();

            var shareLinks = await _context.SHARE_LINKS
                .AsNoTracking()
                .Where(s => s.item_type == "document" && s.item_id == documentId)
                .OrderByDescending(s => s.created_at)
                .Take(10)
                .Select(s => new
                {
                    id = s.share_link_id,
                    token = s.token,
                    access = s.access,
                    permission = s.permission,
                    allowDownload = s.allow_download,
                    viewCount = s.view_count,
                    downloadCount = s.download_count,
                    maxViews = s.max_views,
                    maxDownloads = s.max_downloads,
                    expiresAt = s.expires_at,
                    revokedAt = s.revoked_at,
                    createdAt = s.created_at,
                    isActive = s.revoked_at == null && (s.expires_at == null || s.expires_at > DateTime.UtcNow)
                })
                .ToListAsync();

            var recentViews = await _context.DOCUMENT_VIEWS
                .AsNoTracking()
                .Where(v => v.document_id == documentId)
                .OrderByDescending(v => v.viewed_at)
                .Take(8)
                .Select(v => new
                {
                    type = "view",
                    label = "Lượt xem",
                    at = v.viewed_at,
                    source = v.source
                })
                .ToListAsync();

            var recentDownloads = await _context.DOCUMENT_DOWNLOADS
                .AsNoTracking()
                .Where(d => d.document_id == documentId)
                .OrderByDescending(d => d.downloaded_at)
                .Take(8)
                .Select(d => new
                {
                    type = "download",
                    label = "Lượt tải",
                    at = d.downloaded_at,
                    source = d.source
                })
                .ToListAsync();

            var recentComments = await _context.COMMENTS
                .AsNoTracking()
                .Include(c => c.User)
                .Where(c => c.document_id == documentId && c.deleted_at == null)
                .OrderByDescending(c => c.created_at)
                .Take(8)
                .Select(c => new
                {
                    type = "comment",
                    label = "Bình luận",
                    at = c.created_at,
                    source = c.User != null ? c.User.full_name : null
                })
                .ToListAsync();

            var recentActivity = recentViews
                .Concat(recentDownloads)
                .Concat(recentComments)
                .OrderByDescending(a => a.at)
                .Take(12)
                .ToList();

            return Ok(new
            {
                success = true,
                days = normalizedDays,
                document = new
                {
                    id = document.document_id,
                    title = document.Title,
                    ownerId = document.user_id,
                    ownerName = document.Users != null ? document.Users.full_name : null,
                    uploadedAt = document.uploaded_at,
                    isPublic = document.is_public
                },
                totals = new
                {
                    views = totalViews,
                    downloads = totalDownloads,
                    likes = totalLikes,
                    dislikes = totalDislikes,
                    comments = totalComments,
                    shares = totalShares,
                    versions = totalVersions
                },
                daily,
                sources,
                shareLinks,
                recentActivity
            });
        }

        private async Task<bool> CanViewDocument(Documents document, DecodedTokenResponse? decodedToken)
        {
            if (document.is_public)
                return true;

            if (decodedToken == null)
                return false;

            if (document.user_id == decodedToken.userID || decodedToken.roleID == "admin")
                return true;

            var folderId = await _context.FOLDER_DOCUMENTS
                .AsNoTracking()
                .Where(fd => fd.document_id == document.document_id)
                .Select(fd => (int?)fd.folder_id)
                .FirstOrDefaultAsync();

            return folderId.HasValue && await _folderPermissionService.CanViewFolderAsync(decodedToken.userID, folderId.Value);
        }

        private static bool CanManageDocumentInsights(Documents document, DecodedTokenResponse decodedToken)
        {
            return document.user_id == decodedToken.userID ||
                string.Equals(decodedToken.roleID, "admin", StringComparison.OrdinalIgnoreCase);
        }

        private static string? HashValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }

        private static object Pagination(int currentPage, int pageSize, int totalCount)
        {
            return new
            {
                currentPage,
                pageSize,
                totalCount,
                totalPages = (int)Math.Ceiling(totalCount / (double)Math.Max(pageSize, 1))
            };
        }

        private static object Error(string code, string message, object? details = null) => new { success = false, code, message, details };
    }

    public class RecordViewRequest
    {
        public string? source { get; set; }
    }
}
