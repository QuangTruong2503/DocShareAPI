using DocShareAPI.Data;
using DocShareAPI.Hubs;
using DocShareAPI.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DocShareAPI.Services
{
    public interface INotificationService
    {
        Task CreateAsync(
            Guid recipientUserId,
            string type,
            string title,
            string? message = null,
            Guid? actorUserId = null,
            int? relatedDocumentId = null,
            int? relatedCommentId = null,
            int? relatedReportId = null,
            int? relatedFolderId = null,
            string? targetUrl = null,
            object? metadata = null);

        Task CreateManyAsync(IEnumerable<NotificationCreateRequest> requests);
    }

    public class NotificationCreateRequest
    {
        public Guid recipientUserId { get; set; }
        public Guid? actorUserId { get; set; }
        public required string type { get; set; }
        public required string title { get; set; }
        public string? message { get; set; }
        public int? relatedDocumentId { get; set; }
        public int? relatedCommentId { get; set; }
        public int? relatedReportId { get; set; }
        public int? relatedFolderId { get; set; }
        public string? targetUrl { get; set; }
        public object? metadata { get; set; }
    }

    public class NotificationService : INotificationService
    {
        private readonly DocShareDbContext _context;
        private readonly IHubContext<NotificationsHub> _hubContext;

        public NotificationService(DocShareDbContext context, IHubContext<NotificationsHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public async Task CreateAsync(
            Guid recipientUserId,
            string type,
            string title,
            string? message = null,
            Guid? actorUserId = null,
            int? relatedDocumentId = null,
            int? relatedCommentId = null,
            int? relatedReportId = null,
            int? relatedFolderId = null,
            string? targetUrl = null,
            object? metadata = null)
        {
            await CreateManyAsync(new[]
            {
                new NotificationCreateRequest
                {
                    recipientUserId = recipientUserId,
                    actorUserId = actorUserId,
                    type = type,
                    title = title,
                    message = message,
                    relatedDocumentId = relatedDocumentId,
                    relatedCommentId = relatedCommentId,
                    relatedReportId = relatedReportId,
                    relatedFolderId = relatedFolderId,
                    targetUrl = targetUrl,
                    metadata = metadata
                }
            });
        }

        public async Task CreateManyAsync(IEnumerable<NotificationCreateRequest> requests)
        {
            var now = DateTime.UtcNow;
            var requestList = requests
                .Where(r => r.recipientUserId != Guid.Empty)
                .ToList();

            var disabledInAppUserIds = await _context.NOTIFICATION_SETTINGS
                .AsNoTracking()
                .Where(s => requestList.Select(r => r.recipientUserId).Contains(s.user_id) && !s.in_app_enabled)
                .Select(s => s.user_id)
                .ToListAsync();

            var notifications = requestList
                .Where(r => !disabledInAppUserIds.Contains(r.recipientUserId))
                .Select(r => new Notifications
                {
                    recipient_user_id = r.recipientUserId,
                    actor_user_id = r.actorUserId,
                    type = r.type,
                    title = r.title,
                    message = r.message,
                    related_document_id = r.relatedDocumentId,
                    related_comment_id = r.relatedCommentId,
                    related_report_id = r.relatedReportId,
                    related_folder_id = r.relatedFolderId,
                    target_url = r.targetUrl,
                    metadata = r.metadata == null ? null : JsonSerializer.Serialize(r.metadata),
                    is_read = false,
                    read_at = null,
                    created_at = now,
                    updated_at = now
                })
                .ToList();

            if (notifications.Count == 0)
                return;

            await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
            var ownsTransaction = _context.Database.IsRelational() && _context.Database.CurrentTransaction == null;
            await using var transaction = ownsTransaction ? await _context.Database.BeginTransactionAsync() : null;
            _context.NOTIFICATIONS.AddRange(notifications);
            await _context.SaveChangesAsync();

            _context.AUDIT_LOGS.Add(new AuditLogs { action = "notification.dispatch.pending", entity_type = "notification", metadata = JsonSerializer.Serialize(notifications.Select(n => n.notification_id).ToArray()) });
            await _context.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();
            });
        }

        public static object ToRealtimeResponse(Notifications notification)
        {
            return new
            {
                notification.notification_id,
                notification.recipient_user_id,
                notification.actor_user_id,
                notification.type,
                notification.title,
                notification.message,
                notification.related_document_id,
                notification.related_comment_id,
                notification.related_report_id,
                notification.related_folder_id,
                notification.target_url,
                notification.metadata,
                notification.is_read,
                notification.read_at,
                notification.created_at,
                notification.updated_at
            };
        }
    }
}
