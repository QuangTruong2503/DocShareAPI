using DocShareAPI.Data;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace DocShareAPI.Controllers
{
    [ApiController]
    public class CommentsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly IFolderPermissionService _folderPermissionService;
        private readonly INotificationService _notificationService;
        private readonly IAuditLogService _auditLogService;

        public CommentsController(
            DocShareDbContext context,
            IFolderPermissionService folderPermissionService,
            INotificationService notificationService,
            IAuditLogService auditLogService)
        {
            _context = context;
            _folderPermissionService = folderPermissionService;
            _notificationService = notificationService;
            _auditLogService = auditLogService;
        }

        [AllowAnonymous]
        [HttpGet("/api/documents/{documentId:int}/comments")]
        public async Task<IActionResult> GetDocumentComments(int documentId, [FromQuery] PaginationParams paginationParams)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            var document = await _context.DOCUMENTS.AsNoTracking().FirstOrDefaultAsync(d => d.document_id == documentId && d.deleted_at == null);
            if (document == null)
                return NotFound(Error("DOCUMENT_NOT_FOUND", "Không tìm thấy tài liệu."));

            if (!await CanViewDocument(document, decodedToken))
                return decodedToken == null ? Unauthorized(Error("UNAUTHORIZED", "Bạn cần đăng nhập để xem bình luận.")) : Forbid();

            var page = paginationParams.PageNumber <= 0 ? 1 : paginationParams.PageNumber;
            var pageSize = paginationParams.PageSize <= 0 ? 20 : paginationParams.PageSize;

            var query = _context.COMMENTS
                .AsNoTracking()
                .Include(c => c.User)
                .Where(c => c.document_id == documentId && c.deleted_at == null && c.parent_comment_id == null)
                .OrderByDescending(c => c.created_at);

            var totalCount = await query.CountAsync();
            var comments = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var commentIds = comments.Select(c => c.comment_id).ToList();
            var replies = await _context.COMMENTS
                .AsNoTracking()
                .Include(c => c.User)
                .Where(c => c.parent_comment_id.HasValue && commentIds.Contains(c.parent_comment_id.Value) && c.deleted_at == null)
                .OrderBy(c => c.created_at)
                .ToListAsync();

            return Ok(new
            {
                success = true,
                comments = comments.Select(c => ToResponse(c, decodedToken?.userID, decodedToken?.roleID, replies.Where(r => r.parent_comment_id == c.comment_id))),
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount,
                    totalPages = (int)Math.Ceiling(totalCount / (double)Math.Max(pageSize, 1))
                }
            });
        }

        [HttpPost("/api/documents/{documentId:int}/comments")]
        public async Task<IActionResult> CreateComment(int documentId, [FromBody] CommentRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var content = request.content?.Trim();
            if (string.IsNullOrWhiteSpace(content) || content.Length > 5000)
                return BadRequest(Error("VALIDATION_ERROR", "Nội dung bình luận là bắt buộc và tối đa 5000 ký tự."));

            var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentId && d.deleted_at == null);
            if (document == null)
                return NotFound(Error("DOCUMENT_NOT_FOUND", "Không tìm thấy tài liệu."));

            if (!await CanViewDocument(document, decodedToken))
                return Forbid();

            Comments? parentComment = null;
            int? parentCommentId = null;
            if (request.parentCommentId.HasValue)
            {
                parentComment = await _context.COMMENTS.FirstOrDefaultAsync(c =>
                    c.comment_id == request.parentCommentId.Value &&
                    c.document_id == documentId &&
                    c.deleted_at == null);

                if (parentComment == null)
                    return NotFound(Error("PARENT_COMMENT_NOT_FOUND", "Không tìm thấy bình luận cha."));

                parentCommentId = parentComment.parent_comment_id ?? parentComment.comment_id;
            }

            var now = DateTime.UtcNow;
            var comment = new Comments
            {
                document_id = documentId,
                user_id = decodedToken.userID,
                parent_comment_id = parentCommentId,
                content = content,
                created_at = now,
                updated_at = now
            };

            _context.COMMENTS.Add(comment);
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(decodedToken.userID, "comment.created", "document", documentId.ToString(), new { comment.comment_id }, HttpContext.Connection.RemoteIpAddress?.ToString());

            var mentionedUsers = await GetMentionedUsersAsync(content, decodedToken.userID);
            if (mentionedUsers.Count > 0)
            {
                await _notificationService.CreateManyAsync(mentionedUsers.Select(user => new NotificationCreateRequest
                {
                    recipientUserId = user.user_id,
                    actorUserId = decodedToken.userID,
                    type = "COMMENT_MENTION",
                    title = "Bạn được nhắc trong bình luận",
                    message = $"Bạn được nhắc trong \"{document.Title}\".",
                    relatedDocumentId = document.document_id,
                    relatedCommentId = comment.comment_id,
                    targetUrl = $"/documents/{document.document_id}?comment={comment.comment_id}",
                    metadata = new { mentionedUsername = user.Username }
                }));
            }
            else if (document.user_id != decodedToken.userID)
            {
                await _notificationService.CreateAsync(
                    recipientUserId: document.user_id,
                    actorUserId: decodedToken.userID,
                    type: "DOCUMENT_COMMENT",
                    title: "Tài liệu có bình luận mới",
                    message: $"Có bình luận mới trong \"{document.Title}\".",
                    relatedDocumentId: document.document_id,
                    relatedCommentId: comment.comment_id,
                    targetUrl: $"/documents/{document.document_id}?comment={comment.comment_id}");
            }

            if (mentionedUsers.Count == 0 && parentComment != null && parentComment.user_id != decodedToken.userID && parentComment.user_id != document.user_id)
            {
                await _notificationService.CreateAsync(
                    recipientUserId: parentComment.user_id,
                    actorUserId: decodedToken.userID,
                    type: "COMMENT_REPLY",
                    title: "Có phản hồi bình luận",
                    message: $"Có phản hồi mới trong \"{document.Title}\".",
                    relatedDocumentId: document.document_id,
                    relatedCommentId: comment.comment_id,
                    targetUrl: $"/documents/{document.document_id}?comment={comment.comment_id}");
            }

            var saved = await _context.COMMENTS
                .AsNoTracking()
                .Include(c => c.User)
                .FirstAsync(c => c.comment_id == comment.comment_id);

            return Ok(new { success = true, comment = ToResponse(saved, decodedToken.userID, decodedToken.roleID, Array.Empty<Comments>()) });
        }

        [HttpPatch("/api/comments/{commentId:int}")]
        public async Task<IActionResult> UpdateComment(int commentId, [FromBody] CommentRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var content = request.content?.Trim();
            if (string.IsNullOrWhiteSpace(content) || content.Length > 5000)
                return BadRequest(Error("VALIDATION_ERROR", "Nội dung bình luận là bắt buộc và tối đa 5000 ký tự."));

            var comment = await _context.COMMENTS.FirstOrDefaultAsync(c => c.comment_id == commentId && c.deleted_at == null);
            if (comment == null)
                return NotFound(Error("COMMENT_NOT_FOUND", "Không tìm thấy bình luận."));

            if (comment.user_id != decodedToken.userID && decodedToken.roleID != "admin")
                return Forbid();

            comment.content = content;
            comment.updated_at = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(decodedToken.userID, "comment.updated", "comment", commentId.ToString(), null, HttpContext.Connection.RemoteIpAddress?.ToString());

            return Ok(new { success = true, commentId, content = comment.content, updatedAt = comment.updated_at });
        }

        [HttpDelete("/api/comments/{commentId:int}")]
        public async Task<IActionResult> DeleteComment(int commentId)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var comment = await _context.COMMENTS.FirstOrDefaultAsync(c => c.comment_id == commentId && c.deleted_at == null);
            if (comment == null)
                return NotFound(Error("COMMENT_NOT_FOUND", "Không tìm thấy bình luận."));

            var documentOwnerId = await _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.document_id == comment.document_id)
                .Select(d => d.user_id)
                .FirstOrDefaultAsync();

            if (comment.user_id != decodedToken.userID && documentOwnerId != decodedToken.userID && decodedToken.roleID != "admin")
                return Forbid();

            comment.deleted_at = DateTime.UtcNow;
            comment.updated_at = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(decodedToken.userID, "comment.deleted", "comment", commentId.ToString(), null, HttpContext.Connection.RemoteIpAddress?.ToString());

            return Ok(new { success = true, deletedId = commentId });
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

        private static object ToResponse(Comments comment, Guid? currentUserId, string? currentUserRole, IEnumerable<Comments> replies)
        {
            var canModerate = string.Equals(currentUserRole, "admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(currentUserRole, "moderator", StringComparison.OrdinalIgnoreCase);

            return new
            {
                id = comment.comment_id,
                documentId = comment.document_id,
                parentCommentId = comment.parent_comment_id,
                content = comment.content,
                author = new
                {
                    userId = comment.user_id,
                    username = comment.User?.Username,
                    fullName = comment.User?.full_name,
                    avatarUrl = comment.User?.avatar_url
                },
                canEdit = currentUserId.HasValue && (currentUserId.Value == comment.user_id || canModerate),
                createdAt = comment.created_at,
                updatedAt = comment.updated_at,
                replies = replies.Select(r => ToResponse(r, currentUserId, currentUserRole, Array.Empty<Comments>()))
            };
        }

        private async Task<List<Users>> GetMentionedUsersAsync(string content, Guid actorUserId)
        {
            var usernames = Regex.Matches(content, @"(?<![\w.])@([A-Za-z0-9_.-]{2,50})")
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (usernames.Count == 0)
                return new List<Users>();

            var normalizedUsernames = usernames.Select(username => username.ToLower()).ToList();

            return await _context.USERS
                .AsNoTracking()
                .Where(user => user.user_id != actorUserId && normalizedUsernames.Contains(user.Username.ToLower()))
                .ToListAsync();
        }

        private static object Error(string code, string message, object? details = null) => new { success = false, code, message, details };
    }

    public class CommentRequest
    {
        public string? content { get; set; }
        public int? parentCommentId { get; set; }
    }
}
