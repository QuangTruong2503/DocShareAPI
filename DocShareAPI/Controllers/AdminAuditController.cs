using DocShareAPI.Data;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers
{
    [Route("api/admin/audit-logs")]
    [ApiController]
    public class AdminAuditController : ControllerBase
    {
        private readonly DocShareDbContext _context;

        public AdminAuditController(DocShareDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAuditLogs(
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] string? action = null,
            [FromQuery] string? entityType = null,
            [FromQuery] Guid? actorUserId = null)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));
            if (decodedToken.roleID != "admin")
                return Forbid();

            var page = paginationParams.PageNumber <= 0 ? 1 : paginationParams.PageNumber;
            var pageSize = paginationParams.PageSize <= 0 ? 50 : paginationParams.PageSize;

            var query = _context.AUDIT_LOGS
                .AsNoTracking()
                .Include(a => a.ActorUser)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(action))
                query = query.Where(a => a.action == action.Trim());
            if (!string.IsNullOrWhiteSpace(entityType))
                query = query.Where(a => a.entity_type == entityType.Trim());
            if (actorUserId.HasValue)
                query = query.Where(a => a.actor_user_id == actorUserId.Value);

            query = query.OrderByDescending(a => a.created_at);

            var totalCount = await query.CountAsync();
            var logs = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    a.audit_id,
                    a.actor_user_id,
                    actor = a.ActorUser == null ? null : new
                    {
                        a.ActorUser.user_id,
                        a.ActorUser.Username,
                        a.ActorUser.full_name,
                        a.ActorUser.avatar_url
                    },
                    a.action,
                    a.entity_type,
                    a.entity_id,
                    a.metadata,
                    a.ip_address,
                    a.created_at
                })
                .ToListAsync();

            return Ok(new
            {
                success = true,
                logs,
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount,
                    totalPages = (int)Math.Ceiling(totalCount / (double)Math.Max(pageSize, 1))
                }
            });
        }

        private static object Error(string code, string message, object? details = null) => new { success = false, code, message, details };
    }
}
