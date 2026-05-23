using DocShareAPI.Data;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers
{
    [Route("api/notifications/settings")]
    [ApiController]
    public class NotificationSettingsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly IAuditLogService _auditLogService;

        public NotificationSettingsController(DocShareDbContext context, IAuditLogService auditLogService)
        {
            _context = context;
            _auditLogService = auditLogService;
        }

        [HttpGet]
        public async Task<IActionResult> GetSettings()
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var settings = await GetOrCreateSettings(decodedToken.userID);
            return Ok(new { success = true, settings = ToResponse(settings) });
        }

        [HttpPut]
        public async Task<IActionResult> UpdateSettings([FromBody] NotificationSettingsRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var settings = await GetOrCreateSettings(decodedToken.userID);
            settings.in_app_enabled = request.inAppEnabled;
            settings.email_on_comment = request.emailOnComment;
            settings.email_on_follow = request.emailOnFollow;
            settings.email_on_folder_invite = request.emailOnFolderInvite;
            settings.email_on_report_update = request.emailOnReportUpdate;
            settings.updated_at = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(decodedToken.userID, "notification_settings.updated", "user", decodedToken.userID.ToString(), ToResponse(settings), HttpContext.Connection.RemoteIpAddress?.ToString());

            return Ok(new { success = true, settings = ToResponse(settings) });
        }

        private async Task<NotificationSettings> GetOrCreateSettings(Guid userId)
        {
            var settings = await _context.NOTIFICATION_SETTINGS.FirstOrDefaultAsync(s => s.user_id == userId);
            if (settings != null)
                return settings;

            settings = new NotificationSettings
            {
                user_id = userId,
                in_app_enabled = true,
                email_on_comment = true,
                email_on_follow = true,
                email_on_folder_invite = true,
                email_on_report_update = true,
                updated_at = DateTime.UtcNow
            };

            _context.NOTIFICATION_SETTINGS.Add(settings);
            await _context.SaveChangesAsync();
            return settings;
        }

        private static object ToResponse(NotificationSettings settings)
        {
            return new
            {
                inAppEnabled = settings.in_app_enabled,
                emailOnComment = settings.email_on_comment,
                emailOnFollow = settings.email_on_follow,
                emailOnFolderInvite = settings.email_on_folder_invite,
                emailOnReportUpdate = settings.email_on_report_update,
                updatedAt = settings.updated_at
            };
        }

        private static object Error(string code, string message, object? details = null) => new { success = false, code, message, details };
    }

    public class NotificationSettingsRequest
    {
        public bool inAppEnabled { get; set; } = true;
        public bool emailOnComment { get; set; } = true;
        public bool emailOnFollow { get; set; } = true;
        public bool emailOnFolderInvite { get; set; } = true;
        public bool emailOnReportUpdate { get; set; } = true;
    }
}
