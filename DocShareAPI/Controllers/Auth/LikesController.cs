using Aspose.Pdf.Drawing;
using DocShareAPI.Data;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace DocShareAPI.Controllers.Auth
{
    [Route("api/[controller]")]
    [ApiController]
    public class LikesController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly INotificationService _notificationService;

        public LikesController(DocShareDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        //API truyền trạng thái like hoặc dislike
        [Authorize]
        [HttpPost("reaction")]
        public async Task<IActionResult> SetReaction(int documentId, sbyte reaction)
        {
            if (reaction != 1 && reaction != -1)
                return BadRequest("Reaction must be 1 or -1");

            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
                return Unauthorized();

            var userId = decodedToken.userID;

            var document = await _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.document_id == documentId)
                .Select(d => new
                {
                    d.user_id,
                    d.is_public,
                    d.Title
                })
                .FirstOrDefaultAsync();

            if (document == null)
                return NotFound(new { message = "Document not found" });

            bool canReact = document.is_public ||
                document.user_id == userId ||
                decodedToken.roleID == "admin";

            if (!canReact)
                return Forbid();

            var existing = await _context.LIKES
                .FirstOrDefaultAsync(l =>
                    l.user_id == userId &&
                    l.document_id == documentId);

            int finalReaction = 0;

            if (existing == null)
            {
                // None → Like / Dislike
                _context.LIKES.Add(new Likes
                {
                    user_id = userId,
                    document_id = documentId,
                    reaction = reaction,
                    like_at = DateTime.UtcNow
                });
                finalReaction = reaction;
            }
            else
            {
                if (existing.reaction == reaction)
                {
                    // Same action → remove
                    _context.LIKES.Remove(existing);
                    finalReaction = 0;
                }
                else
                {
                    // Opposite → update (KHÔNG remove)
                    existing.reaction = reaction;
                    existing.like_at = DateTime.UtcNow;
                    finalReaction = reaction;
                }
            }

            await _context.SaveChangesAsync();

            if (finalReaction != 0 && document.user_id != userId)
            {
                var type = finalReaction == 1 ? "LIKE_DOCUMENT" : "DISLIKE_DOCUMENT";
                var notificationAlreadyExists = await _context.NOTIFICATIONS
                    .AsNoTracking()
                    .AnyAsync(n =>
                        n.type == type &&
                        n.actor_user_id == userId &&
                        n.recipient_user_id == document.user_id &&
                        n.related_document_id == documentId);

                if (!notificationAlreadyExists)
                {
                var title = finalReaction == 1
                    ? "Tài liệu của bạn có lượt thích mới"
                    : "Tài liệu của bạn có lượt không thích mới";

                await _notificationService.CreateAsync(
                    recipientUserId: document.user_id,
                    actorUserId: userId,
                    type: type,
                    title: title,
                    message: $"Một người dùng đã {(finalReaction == 1 ? "thích" : "không thích")} tài liệu \"{document.Title}\".",
                    relatedDocumentId: documentId,
                    targetUrl: $"/documents/{documentId}",
                    metadata: new { reaction = finalReaction });
                }
            }

            var counts = await _context.LIKES
                .Where(l => l.document_id == documentId)
                .GroupBy(l => l.document_id)
                .Select(g => new
                {
                    likeCount = g.Count(l => l.reaction == 1),
                    dislikeCount = g.Count(l => l.reaction == -1)
                })
                .FirstOrDefaultAsync();

            return Ok(new
            {
                reaction = finalReaction,
                likeCount = counts?.likeCount ?? 0,
                dislikeCount = counts?.dislikeCount ?? 0
            });
        }
    }
}
