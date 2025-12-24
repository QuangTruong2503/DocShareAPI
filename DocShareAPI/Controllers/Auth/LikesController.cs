using Aspose.Pdf.Drawing;
using DocShareAPI.Data;
using DocShareAPI.Models;
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
        public LikesController(DocShareDbContext context)
        {
            _context = context;
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
            var likeCount = await _context.LIKES
                .CountAsync(l => l.document_id == documentId && l.reaction == 1);

            var dislikeCount = await _context.LIKES
                .CountAsync(l => l.document_id == documentId && l.reaction == -1);

            return Ok(new
            {
                reaction = finalReaction,
                likeCount,
                dislikeCount
            });
        }
    }
}
