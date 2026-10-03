using DocShareAPI.Data;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers
{
    [ApiController]
    public class StorageController : ControllerBase
    {
        private const long DefaultStorageLimitBytes = 10L * 1024 * 1024 * 1024;

        private readonly DocShareDbContext _context;
        private readonly IAuditLogService _auditLogService;

        public StorageController(DocShareDbContext context, IAuditLogService auditLogService)
        {
            _context = context;
            _auditLogService = auditLogService;
        }

        [HttpGet("/api/users/me/storage")]
        public async Task<IActionResult> GetMyStorage()
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            return Ok(new { success = true, storage = await BuildStorage(decodedToken.userID) });
        }

        [HttpPatch("/api/admin/users/{userId:guid}/storage")]
        public async Task<IActionResult> UpdateUserStorageLimit(Guid userId, [FromBody] StorageLimitRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));
            if (decodedToken.roleID != "admin")
                return Forbid();

            if (request.storageLimitBytes <= 0)
                return BadRequest(Error("VALIDATION_ERROR", "storageLimitBytes phải lớn hơn 0."));

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == userId);
            if (user == null)
                return NotFound(Error("USER_NOT_FOUND", "Không tìm thấy người dùng."));

            user.storage_limit_bytes = request.storageLimitBytes;
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(decodedToken.userID, "storage_limit.updated", "user", userId.ToString(), new { request.storageLimitBytes }, HttpContext.Connection.RemoteIpAddress?.ToString());

            return Ok(new { success = true, storage = await BuildStorage(userId) });
        }

        private async Task<object> BuildStorage(Guid userId)
        {
            var limit = await _context.USERS
                .AsNoTracking()
                .Where(u => u.user_id == userId)
                .Select(u => u.storage_limit_bytes)
                .FirstOrDefaultAsync() ?? DefaultStorageLimitBytes;

            var used = await StorageAccounting.UsedAsync(_context, userId);

            return new
            {
                usedBytes = used,
                limitBytes = limit,
                remainingBytes = Math.Max(0, limit - used),
                usageRatio = limit <= 0 ? 1 : Math.Round(used / (double)limit, 4)
            };
        }

        private static object Error(string code, string message, object? details = null) => new { success = false, code, message, details };
    }

    public class StorageLimitRequest
    {
        public long storageLimitBytes { get; set; }
    }
}
