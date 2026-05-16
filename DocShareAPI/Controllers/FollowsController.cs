using DocShareAPI.Data;
using DocShareAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers
{
    [Route("api/follows")]
    [ApiController]
    public class FollowsController : ControllerBase
    {
        private readonly DocShareDbContext _context;

        public FollowsController(DocShareDbContext context)
        {
            _context = context;
        }

        [HttpPost("{followingID:guid}")]
        public async Task<IActionResult> FollowUser(Guid followingID)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }

            if (decodedToken.userID == followingID)
            {
                return BadRequest(new { message = "You cannot follow yourself." });
            }

            var targetUser = await _context.USERS
                .AsNoTracking()
                .Where(u => u.user_id == followingID)
                .Select(u => new
                {
                    u.user_id,
                    u.Username,
                    u.full_name,
                    u.avatar_url
                })
                .FirstOrDefaultAsync();

            if (targetUser == null)
            {
                return NotFound(new { message = "User not found." });
            }

            var alreadyFollowing = await _context.FOLLOWS.AnyAsync(f =>
                f.follower_id == decodedToken.userID &&
                f.following_id == followingID);

            if (alreadyFollowing)
            {
                return Conflict(new { message = "You are already following this user." });
            }

            var follow = new Follows
            {
                follower_id = decodedToken.userID,
                following_id = followingID,
                created_at = DateTime.UtcNow
            };

            _context.FOLLOWS.Add(follow);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Followed user successfully.",
                is_following = true,
                follow = new
                {
                    follow.follower_id,
                    follow.following_id,
                    follow.created_at,
                    following = targetUser
                }
            });
        }

        [HttpDelete("{followingID:guid}")]
        public async Task<IActionResult> UnfollowUser(Guid followingID)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }

            var follow = await _context.FOLLOWS.FirstOrDefaultAsync(f =>
                f.follower_id == decodedToken.userID &&
                f.following_id == followingID);

            if (follow == null)
            {
                return NotFound(new { message = "Follow relationship not found." });
            }

            _context.FOLLOWS.Remove(follow);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Unfollowed user successfully.",
                is_following = false,
                follower_id = decodedToken.userID,
                following_id = followingID
            });
        }

        [HttpDelete("followers/{followerID:guid}")]
        public async Task<IActionResult> RemoveFollower(Guid followerID)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }

            if (decodedToken.userID == followerID)
            {
                return BadRequest(new { message = "You cannot remove yourself from your followers." });
            }

            var follow = await _context.FOLLOWS.FirstOrDefaultAsync(f =>
                f.follower_id == followerID &&
                f.following_id == decodedToken.userID);

            if (follow == null)
            {
                return NotFound(new { message = "Follower relationship not found." });
            }

            _context.FOLLOWS.Remove(follow);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Follower removed successfully.",
                follower_id = followerID,
                following_id = decodedToken.userID
            });
        }
    }
}
