using DocShareAPI.Data;
using DocShareAPI.DataTransferObject.Reports;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers.Auth
{
    [Authorize]
    [Route("api/reports")]
    [ApiController]
    public class ReportsController : ControllerBase
    {
        private static readonly string[] ReportStatuses =
        {
            "Chờ giải quyết",
            "Đang xử lý",
            "Đã xử lý",
            "Từ chối"
        };

        private static readonly string[] SuggestedReasons =
        {
            "Nội dung vi phạm bản quyền",
            "Nội dung sai sự thật",
            "Tài liệu spam hoặc quảng cáo",
            "Tài liệu không phù hợp",
            "File lỗi hoặc không thể xem",
            "Lý do khác"
        };

        private readonly DocShareDbContext _context;
        private readonly INotificationService _notificationService;

        public ReportsController(DocShareDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        [HttpGet("options")]
        public IActionResult GetReportOptions()
        {
            return Ok(new
            {
                success = true,
                data = new
                {
                    statuses = ReportStatuses,
                    suggestedReasons = SuggestedReasons,
                    reasonRules = new
                    {
                        minLength = 5,
                        maxLength = 1000
                    }
                }
            });
        }

        [HttpPost]
        public async Task<IActionResult> CreateReport([FromBody] CreateReportRequest request)
        {
            var decodedToken = GetDecodedToken();
            if (decodedToken == null)
                return Unauthorized(new { success = false, message = "Authentication required." });

            if (request.DocumentId <= 0)
                return BadRequest(new { success = false, message = "DocumentId is required." });

            if (string.IsNullOrWhiteSpace(request.Reason))
                return BadRequest(new { success = false, message = "Reason is required." });

            var reason = request.Reason.Trim();
            if (reason.Length < 5 || reason.Length > 1000)
                return BadRequest(new { success = false, message = "Reason must be between 5 and 1000 characters." });

            var document = await _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.document_id == request.DocumentId)
                .Select(d => new
                {
                    d.document_id,
                    d.user_id,
                    d.Title,
                    d.thumbnail_url,
                    d.is_public
                })
                .FirstOrDefaultAsync();

            if (document == null)
                return NotFound(new { success = false, message = "Document not found." });

            var isOwner = document.user_id == decodedToken.userID;
            var isAdmin = string.Equals(decodedToken.roleID, "admin", StringComparison.OrdinalIgnoreCase);
            var canReport = document.is_public || isOwner || isAdmin;

            if (!canReport)
                return Forbid();

            if (isOwner)
                return BadRequest(new { success = false, message = "You cannot report your own document." });

            var activeStatuses = new[] { "Chờ giải quyết", "Đang xử lý" };
            var existingReport = await _context.REPORTS
                .AsNoTracking()
                .Where(r =>
                    r.user_id == decodedToken.userID &&
                    r.document_id == request.DocumentId &&
                    activeStatuses.Contains(r.Status))
                .Select(r => new
                {
                    r.report_id,
                    r.document_id,
                    r.Reason,
                    r.Status,
                    r.created_at
                })
                .FirstOrDefaultAsync();

            if (existingReport != null)
            {
                return Conflict(new
                {
                    success = false,
                    message = "You already have an active report for this document.",
                    data = existingReport
                });
            }

            var report = new Reports
            {
                user_id = decodedToken.userID,
                document_id = request.DocumentId,
                Reason = reason,
                Status = "Chờ giải quyết",
                created_at = DateTime.UtcNow
            };

            _context.REPORTS.Add(report);
            await _context.SaveChangesAsync();

            var adminIds = await _context.USERS
                .AsNoTracking()
                .Where(u => u.Role == "admin" && u.user_id != decodedToken.userID)
                .Select(u => u.user_id)
                .ToListAsync();

            var notifications = adminIds
                .Select(adminId => new NotificationCreateRequest
                {
                    recipientUserId = adminId,
                    actorUserId = decodedToken.userID,
                    type = "REPORT_CREATED",
                    title = "Có báo cáo tài liệu mới",
                    message = $"Tài liệu \"{document.Title}\" vừa bị báo cáo.",
                    relatedDocumentId = document.document_id,
                    relatedReportId = report.report_id,
                    targetUrl = $"/admin/reports/{report.report_id}",
                    metadata = new { reason }
                })
                .ToList();

            if (document.user_id != decodedToken.userID)
            {
                notifications.Add(new NotificationCreateRequest
                {
                    recipientUserId = document.user_id,
                    actorUserId = decodedToken.userID,
                    type = "REPORT_CREATED",
                    title = "Tài liệu của bạn có báo cáo mới",
                    message = $"Tài liệu \"{document.Title}\" vừa bị báo cáo.",
                    relatedDocumentId = document.document_id,
                    relatedReportId = report.report_id,
                    targetUrl = $"/documents/{document.document_id}",
                    metadata = new { reason }
                });
            }

            await _notificationService.CreateManyAsync(notifications);

            var response = new
            {
                report.report_id,
                report.user_id,
                report.document_id,
                report.Reason,
                report.Status,
                report.created_at,
                document = new
                {
                    document.document_id,
                    document.Title,
                    document.thumbnail_url,
                    document.is_public
                }
            };

            return CreatedAtAction(nameof(GetMyReportDetail), new { reportId = report.report_id }, new
            {
                success = true,
                message = "Report created successfully.",
                data = response
            });
        }

        [HttpGet("my")]
        public async Task<IActionResult> GetMyReports(
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] string? status,
            [FromQuery] int? documentId)
        {
            var decodedToken = GetDecodedToken();
            if (decodedToken == null)
                return Unauthorized(new { success = false, message = "Authentication required." });

            var query = _context.REPORTS
                .AsNoTracking()
                .Where(r => r.user_id == decodedToken.userID);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(r => r.Status == status.Trim());

            if (documentId.HasValue)
                query = query.Where(r => r.document_id == documentId.Value);

            var reports = await query
                .OrderByDescending(r => r.created_at)
                .Select(r => new
                {
                    r.report_id,
                    r.user_id,
                    r.document_id,
                    r.Reason,
                    r.Status,
                    r.created_at,
                    document = new
                    {
                        r.Documents.document_id,
                        r.Documents.Title,
                        r.Documents.thumbnail_url,
                        r.Documents.is_public
                    }
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(ToPagedResponse(reports));
        }

        [HttpGet("my/{reportId:int}")]
        public async Task<IActionResult> GetMyReportDetail(int reportId)
        {
            var decodedToken = GetDecodedToken();
            if (decodedToken == null)
                return Unauthorized(new { success = false, message = "Authentication required." });

            var report = await _context.REPORTS
                .AsNoTracking()
                .Where(r => r.report_id == reportId && r.user_id == decodedToken.userID)
                .Select(r => new
                {
                    r.report_id,
                    r.user_id,
                    r.document_id,
                    r.Reason,
                    r.Status,
                    r.created_at,
                    reporter = new
                    {
                        r.Users.user_id,
                        r.Users.Username,
                        r.Users.full_name,
                        r.Users.Email,
                        r.Users.avatar_url
                    },
                    document = new
                    {
                        r.Documents.document_id,
                        r.Documents.Title,
                        r.Documents.Description,
                        r.Documents.thumbnail_url,
                        r.Documents.file_url,
                        r.Documents.is_public,
                        r.Documents.uploaded_at,
                        r.Documents.download_count,
                        owner = r.Documents.Users == null ? null : new
                        {
                            r.Documents.Users.user_id,
                            r.Documents.Users.Username,
                            r.Documents.Users.full_name,
                            r.Documents.Users.avatar_url
                        }
                    }
                })
                .FirstOrDefaultAsync();

            if (report == null)
                return NotFound(new { success = false, message = "Report not found." });

            return Ok(new { success = true, data = report });
        }

        private DecodedTokenResponse? GetDecodedToken()
        {
            return HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
        }

        private static object ToPagedResponse<T>(PagedList<T> pagedData)
        {
            return new
            {
                success = true,
                data = pagedData,
                pagination = new
                {
                    pagedData.CurrentPage,
                    pagedData.PageSize,
                    pagedData.TotalCount,
                    pagedData.TotalPages
                }
            };
        }
    }
}
