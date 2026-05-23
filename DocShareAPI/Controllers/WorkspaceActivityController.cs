using DocShareAPI.Data;
using DocShareAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers
{
    [Route("api/workspace")]
    [ApiController]
    public class WorkspaceActivityController : ControllerBase
    {
        private readonly DocShareDbContext _context;

        public WorkspaceActivityController(DocShareDbContext context)
        {
            _context = context;
        }

        [HttpGet("activity")]
        public async Task<IActionResult> GetActivity([FromQuery] int limit = 12)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var normalizedLimit = Math.Clamp(limit, 5, 50);
            var userId = decodedToken.userID;
            var isAdmin = string.Equals(decodedToken.roleID, "admin", StringComparison.OrdinalIgnoreCase);

            var ownedDocumentIds = _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.user_id == userId)
                .Select(d => d.document_id);

            var summary = new
            {
                myFolders = await _context.FOLDERS.AsNoTracking().CountAsync(f => f.owner_user_id == userId && f.deleted_at == null),
                sharedFolders = await _context.FOLDER_MEMBERS.AsNoTracking().CountAsync(m => m.user_id == userId && m.Folder != null && m.Folder.deleted_at == null),
                activeShareLinks = await _context.SHARE_LINKS.AsNoTracking().CountAsync(s => s.owner_user_id == userId && s.revoked_at == null && (s.expires_at == null || s.expires_at > DateTime.UtcNow)),
                pendingInvites = await _context.FOLDER_INVITES.AsNoTracking().CountAsync(i => i.status == "pending" && (i.invitee_user_id == userId || i.InviteeUser!.user_id == userId)),
                unreadNotifications = await _context.NOTIFICATIONS.AsNoTracking().CountAsync(n => n.recipient_user_id == userId && !n.is_read),
                activeReports = await _context.REPORTS.AsNoTracking().CountAsync(r => r.user_id == userId && r.Status != "Đã giải quyết" && r.Status != "Đã hủy"),
                documentVersions = await _context.DOCUMENT_VERSIONS.AsNoTracking().CountAsync(v => ownedDocumentIds.Contains(v.document_id) || v.uploaded_by == userId),
                auditEvents = await _context.AUDIT_LOGS.AsNoTracking().CountAsync(a => a.actor_user_id == userId),
                moderationQueue = isAdmin
                    ? await _context.REPORTS.AsNoTracking().CountAsync(r => r.Status == "Chờ giải quyết")
                    : 0
            };

            var notifications = await _context.NOTIFICATIONS
                .AsNoTracking()
                .Where(n => n.recipient_user_id == userId)
                .OrderByDescending(n => n.created_at)
                .Take(normalizedLimit)
                .Select(n => new WorkspaceActivityItem
                {
                    id = n.notification_id.ToString(),
                    type = "notification",
                    title = n.title,
                    message = n.message,
                    status = n.is_read ? "read" : "unread",
                    targetUrl = n.target_url,
                    createdAt = n.created_at
                })
                .ToListAsync();

            var shareLinks = await _context.SHARE_LINKS
                .AsNoTracking()
                .Where(s => s.owner_user_id == userId)
                .OrderByDescending(s => s.updated_at)
                .Take(normalizedLimit)
                .Select(s => new WorkspaceActivityItem
                {
                    id = s.share_link_id.ToString(),
                    type = "sharing",
                    title = s.item_type == "folder" ? "Link chia sẻ thư mục" : "Link chia sẻ tài liệu",
                    message = $"{s.view_count} lượt xem, {s.download_count} lượt tải",
                    status = s.revoked_at != null ? "revoked" : (s.expires_at != null && s.expires_at <= DateTime.UtcNow ? "expired" : "active"),
                    targetUrl = $"/s/{s.token}",
                    createdAt = s.updated_at
                })
                .ToListAsync();

            var versions = await _context.DOCUMENT_VERSIONS
                .AsNoTracking()
                .Include(v => v.Document)
                .Where(v => ownedDocumentIds.Contains(v.document_id) || v.uploaded_by == userId)
                .OrderByDescending(v => v.created_at)
                .Take(normalizedLimit)
                .Select(v => new WorkspaceActivityItem
                {
                    id = v.version_id.ToString(),
                    type = "versioning",
                    title = v.Document == null ? $"Phiên bản v{v.version_number}" : $"{v.Document.Title} · v{v.version_number}",
                    message = string.IsNullOrWhiteSpace(v.change_note) ? "Tạo phiên bản mới" : v.change_note,
                    status = "created",
                    targetUrl = $"/document/{v.document_id}",
                    createdAt = v.created_at
                })
                .ToListAsync();

            var reports = await _context.REPORTS
                .AsNoTracking()
                .Include(r => r.Documents)
                .Where(r => r.user_id == userId || (isAdmin && r.Status == "Chờ giải quyết"))
                .OrderByDescending(r => r.created_at)
                .Take(normalizedLimit)
                .Select(r => new WorkspaceActivityItem
                {
                    id = r.report_id.ToString(),
                    type = "moderation",
                    title = r.Documents == null ? $"Report #{r.report_id}" : r.Documents.Title,
                    message = r.Reason,
                    status = r.Status,
                    targetUrl = isAdmin ? $"/admin?tab=reports&reportId={r.report_id}" : $"/my-reports/{r.report_id}",
                    createdAt = r.created_at
                })
                .ToListAsync();

            var invites = await _context.FOLDER_INVITES
                .AsNoTracking()
                .Include(i => i.Folder)
                .Where(i => i.invitee_user_id == userId || i.inviter_user_id == userId)
                .OrderByDescending(i => i.updated_at)
                .Take(normalizedLimit)
                .Select(i => new WorkspaceActivityItem
                {
                    id = i.invite_id.ToString(),
                    type = "workspace",
                    title = i.Folder == null ? "Lời mời thư mục" : i.Folder.name,
                    message = i.inviter_user_id == userId ? "Bạn đã gửi lời mời truy cập" : "Bạn được mời vào thư mục",
                    status = i.status,
                    targetUrl = i.invitee_user_id == userId ? "/folder-invites" : $"/library/folders/{i.folder_id}/invites",
                    createdAt = i.updated_at
                })
                .ToListAsync();

            var auditLogs = await _context.AUDIT_LOGS
                .AsNoTracking()
                .Where(a => a.actor_user_id == userId || isAdmin)
                .OrderByDescending(a => a.created_at)
                .Take(normalizedLimit)
                .Select(a => new WorkspaceActivityItem
                {
                    id = a.audit_id.ToString(),
                    type = "audit",
                    title = a.action,
                    message = $"{a.entity_type}{(a.entity_id == null ? "" : $" #{a.entity_id}")}",
                    status = "logged",
                    targetUrl = isAdmin ? "/admin?tab=audit" : null,
                    createdAt = a.created_at
                })
                .ToListAsync();

            var timeline = notifications
                .Concat(shareLinks)
                .Concat(versions)
                .Concat(reports)
                .Concat(invites)
                .Concat(auditLogs)
                .OrderByDescending(item => item.createdAt)
                .Take(normalizedLimit)
                .ToList();

            return Ok(new
            {
                success = true,
                summary,
                counts = new
                {
                    activity = summary.unreadNotifications + summary.activeReports + summary.pendingInvites,
                    notifications = summary.unreadNotifications,
                    reports = summary.activeReports,
                    invites = summary.pendingInvites,
                    shareLinks = summary.activeShareLinks
                },
                sections = new
                {
                    notifications,
                    shareLinks,
                    versions,
                    reports,
                    invites,
                    auditLogs
                },
                timeline
            });
        }

        private static object Error(string code, string message, object? details = null) => new { success = false, code, message, details };

        private class WorkspaceActivityItem
        {
            public required string id { get; set; }
            public required string type { get; set; }
            public required string title { get; set; }
            public string? message { get; set; }
            public string? status { get; set; }
            public string? targetUrl { get; set; }
            public DateTime createdAt { get; set; }
        }
    }
}
