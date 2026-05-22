using DocShareAPI.Data;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers
{
    [Route("api/follows")]
    [ApiController]
    public class FollowsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly INotificationService _notificationService;

        public FollowsController(DocShareDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
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
                return BadRequest(new { message = "Bạn không thể tự theo dõi chính mình." });
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
                return NotFound(new { message = "Không tìm thấy người dùng." });
            }

            var alreadyFollowing = await _context.FOLLOWS.AnyAsync(f =>
                f.follower_id == decodedToken.userID &&
                f.following_id == followingID);

            if (alreadyFollowing)
            {
                return Conflict(new { message = "Bạn đã theo dõi người dùng này." });
            }

            var follow = new Follows
            {
                follower_id = decodedToken.userID,
                following_id = followingID,
                created_at = DateTime.UtcNow
            };

            _context.FOLLOWS.Add(follow);
            await _context.SaveChangesAsync();

            var notificationAlreadyExists = await _context.NOTIFICATIONS
                .AsNoTracking()
                .AnyAsync(n =>
                    n.type == "FOLLOW_USER" &&
                    n.actor_user_id == decodedToken.userID &&
                    n.recipient_user_id == followingID);

            if (!notificationAlreadyExists)
            {
                await _notificationService.CreateAsync(
                    recipientUserId: followingID,
                    actorUserId: decodedToken.userID,
                    type: "FOLLOW_USER",
                    title: "Bạn có người theo dõi mới",
                    message: "Một người dùng vừa theo dõi bạn.",
                    targetUrl: $"/users/{decodedToken.userID}");
            }

            return Ok(new
            {
                message = "Theo dõi người dùng thành công.",
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
                return NotFound(new { message = "Không tìm thấy quan hệ theo dõi." });
            }

            _context.FOLLOWS.Remove(follow);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Bỏ theo dõi người dùng thành công.",
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
                return BadRequest(new { message = "Bạn không thể tự xóa mình khỏi danh sách người theo dõi." });
            }

            var follow = await _context.FOLLOWS.FirstOrDefaultAsync(f =>
                f.follower_id == followerID &&
                f.following_id == decodedToken.userID);

            if (follow == null)
            {
                return NotFound(new { message = "Không tìm thấy quan hệ người theo dõi." });
            }

            _context.FOLLOWS.Remove(follow);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Xóa người theo dõi thành công.",
                follower_id = followerID,
                following_id = decodedToken.userID
            });
        }
    }
}
