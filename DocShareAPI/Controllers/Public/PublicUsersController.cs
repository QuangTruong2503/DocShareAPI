using DocShareAPI.Data;
using DocShareAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers.Public
{
    [Route("api/public")]
    [ApiController]
    public class PublicUsersController : ControllerBase
    {
        private readonly DocShareDbContext _context;

        public PublicUsersController(DocShareDbContext context)
        {
            _context = context;
        }

        [HttpGet("profile/{userID:guid}")]
        public async Task<IActionResult> GetPublicProfile(Guid userID)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;

            var profile = await _context.USERS
                .AsNoTracking()
                .Where(u => u.user_id == userID)
                .Select(u => new
                {
                    u.user_id,
                    u.Username,
                    u.full_name,
                    u.avatar_url,
                    u.created_at,
                    public_document_count = _context.DOCUMENTS.Count(d => d.user_id == u.user_id && d.is_public),
                    public_collection_count = _context.COLLECTIONS.Count(c => c.user_id == u.user_id && c.is_public),
                    follower_count = _context.FOLLOWS.Count(f => f.following_id == u.user_id),
                    following_count = _context.FOLLOWS.Count(f => f.follower_id == u.user_id),
                    is_following = decodedToken == null
                        ? (bool?)null
                        : _context.FOLLOWS.Any(f =>
                            f.follower_id == decodedToken.userID &&
                            f.following_id == u.user_id)
                })
                .FirstOrDefaultAsync();

            if (profile == null)
            {
                return NotFound(new { message = "User not found." });
            }

            return Ok(profile);
        }
    }
}
