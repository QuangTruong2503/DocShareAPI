using DocShareAPI.Data;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Hubs;
using DocShareAPI.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers
{
    [Route("api/notifications")]
    [ApiController]
    public class NotificationsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly IHubContext<NotificationsHub> _hubContext;

        public NotificationsController(DocShareDbContext context, IHubContext<NotificationsHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetMyNotifications(
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] bool? isRead = null,
            [FromQuery] string? type = null)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
                return Unauthorized();

            var query = _context.NOTIFICATIONS
                .AsNoTracking()
                .Where(n => n.recipient_user_id == decodedToken.userID);

            if (isRead.HasValue)
                query = query.Where(n => n.is_read == isRead.Value);

            if (!string.IsNullOrWhiteSpace(type))
                query = query.Where(n => n.type == type.Trim());

            var notifications = await query
                .OrderByDescending(n => n.created_at)
                .Select(n => new
                {
                    n.notification_id,
                    n.recipient_user_id,
                    n.actor_user_id,
                    actor = n.ActorUser == null ? null : new
                    {
                        n.ActorUser.user_id,
                        n.ActorUser.Username,
                        n.ActorUser.full_name,
                        n.ActorUser.avatar_url
                    },
                    n.type,
                    n.title,
                    n.message,
                    n.related_document_id,
                    document = n.RelatedDocument == null ? null : new
                    {
                        n.RelatedDocument.document_id,
                        n.RelatedDocument.Title,
                        n.RelatedDocument.thumbnail_url,
                        n.RelatedDocument.is_public
                    },
                    n.related_comment_id,
                    n.related_report_id,
                    n.related_folder_id,
                    folder = n.RelatedFolder == null ? null : new
                    {
                        n.RelatedFolder.folder_id,
                        n.RelatedFolder.name,
                        n.RelatedFolder.visibility
                    },
                    n.target_url,
                    n.metadata,
                    n.is_read,
                    n.read_at,
                    n.created_at,
                    n.updated_at
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(new
            {
                success = true,
                data = notifications,
                pagination = new
                {
                    notifications.CurrentPage,
                    notifications.PageSize,
                    notifications.TotalCount,
                    notifications.TotalPages
                }
            });
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
                return Unauthorized();

            var count = await _context.NOTIFICATIONS
                .AsNoTracking()
                .CountAsync(n => n.recipient_user_id == decodedToken.userID && !n.is_read);

            return Ok(new { success = true, unread_count = count });
        }

        [HttpPatch("{notificationId:int}/read")]
        public async Task<IActionResult> MarkAsRead(int notificationId)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
                return Unauthorized();

            var notification = await _context.NOTIFICATIONS
                .FirstOrDefaultAsync(n =>
                    n.notification_id == notificationId &&
                    n.recipient_user_id == decodedToken.userID);

            if (notification == null)
                return NotFound(new { success = false, message = "Không tìm thấy thông báo." });

            if (!notification.is_read)
            {
                notification.is_read = true;
                notification.read_at = DateTime.UtcNow;
                notification.updated_at = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await SendUnreadCountChanged(decodedToken.userID);
            }

            return Ok(new { success = true, notification_id = notificationId, is_read = true });
        }

        [HttpPatch("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
                return Unauthorized();

            var now = DateTime.UtcNow;
            var updated = await _context.NOTIFICATIONS
                .Where(n => n.recipient_user_id == decodedToken.userID && !n.is_read)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(n => n.is_read, true)
                    .SetProperty(n => n.read_at, now)
                    .SetProperty(n => n.updated_at, now));

            await SendUnreadCountChanged(decodedToken.userID);

            return Ok(new { success = true, updated_count = updated });
        }

        [HttpDelete("{notificationId:int}")]
        public async Task<IActionResult> DeleteNotification(int notificationId)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
                return Unauthorized();

            var notification = await _context.NOTIFICATIONS
                .FirstOrDefaultAsync(n =>
                    n.notification_id == notificationId &&
                    n.recipient_user_id == decodedToken.userID);

            if (notification == null)
                return NotFound(new { success = false, message = "Không tìm thấy thông báo." });

            _context.NOTIFICATIONS.Remove(notification);
            await _context.SaveChangesAsync();
            await SendUnreadCountChanged(decodedToken.userID);

            return Ok(new { success = true, deleted_notification_id = notificationId });
        }

        private async Task SendUnreadCountChanged(Guid userId)
        {
            var unreadCount = await _context.NOTIFICATIONS
                .AsNoTracking()
                .CountAsync(n => n.recipient_user_id == userId && !n.is_read);

            await _hubContext.Clients
                .Group(NotificationsHub.GetUserGroupName(userId))
                .SendAsync("UnreadCountChanged", new { unread_count = unreadCount });
        }
    }
}
