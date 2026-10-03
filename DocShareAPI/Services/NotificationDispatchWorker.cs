using System.Text.Json;
using DocShareAPI.Data;
using DocShareAPI.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Services;

public class NotificationDispatchWorker(IServiceScopeFactory scopes, IHubContext<NotificationsHub> hub, ILogger<NotificationDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DocShareDbContext>();
                await using var mutex = await DatabaseMutex.Acquire(db, "docshare:notification-outbox", 0);
                var jobs = await db.AUDIT_LOGS.Where(a => a.action == "notification.dispatch.pending").OrderBy(a => a.audit_id).Take(50).ToListAsync(stoppingToken);
                foreach (var job in jobs)
                {
                    var ids = JsonSerializer.Deserialize<int[]>(job.metadata ?? "[]") ?? [];
                    var notifications = await db.NOTIFICATIONS.AsNoTracking().Where(n => ids.Contains(n.notification_id)).ToListAsync(stoppingToken);
                    foreach (var notification in notifications)
                        await hub.Clients.Group(NotificationsHub.GetUserGroupName(notification.recipient_user_id)).SendAsync("ReceiveNotification", NotificationService.ToRealtimeResponse(notification), stoppingToken);
                    foreach (var recipient in notifications.Select(n => n.recipient_user_id).Distinct())
                    {
                        var unread = await db.NOTIFICATIONS.CountAsync(n => n.recipient_user_id == recipient && !n.is_read, stoppingToken);
                        await hub.Clients.Group(NotificationsHub.GetUserGroupName(recipient)).SendAsync("UnreadCountChanged", new { unread_count = unread }, stoppingToken);
                    }
                    job.action = "notification.dispatch.completed"; await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (TimeoutException) { /* Another instance is dispatching this outbox. */ }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex,"Notification dispatch failed; committed events remain queued for retry."); }
        }
    }
}
