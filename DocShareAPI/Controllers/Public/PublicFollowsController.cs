using DocShareAPI.Data;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers.Public
{
    [Route("api/public")]
    [ApiController]
    public class PublicFollowsController : ControllerBase
    {
        private readonly DocShareDbContext _context;

        public PublicFollowsController(DocShareDbContext context)
        {
            _context = context;
        }

        [HttpGet("follows/{followerID:guid}/{followingID:guid}")]
        public async Task<IActionResult> GetFollowDetail(Guid followerID, Guid followingID)
        {
            var follow = await _context.FOLLOWS
                .AsNoTracking()
                .Where(f => f.follower_id == followerID && f.following_id == followingID)
                .Select(f => new
                {
                    f.follower_id,
                    f.following_id,
                    f.created_at,
                    follower = new
                    {
                        f.Follower.user_id,
                        f.Follower.Username,
                        f.Follower.full_name,
                        f.Follower.avatar_url
                    },
                    following = new
                    {
                        f.Following.user_id,
                        f.Following.Username,
                        f.Following.full_name,
                        f.Following.avatar_url
                    }
                })
                .FirstOrDefaultAsync();

            if (follow == null)
            {
                return NotFound(new { message = "Không tìm thấy quan hệ theo dõi." });
            }

            return Ok(follow);
        }

        [HttpGet("users/{userID:guid}/followers")]
        public async Task<IActionResult> GetUserFollowers(
            Guid userID,
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] string? search = null)
        {
            var userExists = await _context.USERS
                .AsNoTracking()
                .AnyAsync(u => u.user_id == userID);

            if (!userExists)
            {
                return NotFound(new { message = "Không tìm thấy người dùng." });
            }

            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            var viewerID = decodedToken?.userID;
            var normalizedSearch = search?.Trim().ToLower();

            var query = _context.FOLLOWS
                .AsNoTracking()
                .Where(f => f.following_id == userID);

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                query = query.Where(f =>
                    f.Follower.Username.ToLower().Contains(normalizedSearch) ||
                    (f.Follower.full_name != null && f.Follower.full_name.ToLower().Contains(normalizedSearch)));
            }

            var followers = await query
                .OrderByDescending(f => f.created_at)
                .Select(f => new
                {
                    f.created_at,
                    user = new
                    {
                        f.Follower.user_id,
                        f.Follower.Username,
                        f.Follower.full_name,
                        f.Follower.avatar_url
                    },
                    is_following = viewerID == null
                        ? (bool?)null
                        : _context.FOLLOWS.Any(viewerFollow =>
                            viewerFollow.follower_id == viewerID.Value &&
                            viewerFollow.following_id == f.Follower.user_id)
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(new
            {
                user_id = userID,
                search,
                followers,
                pagination = new
                {
                    followers.CurrentPage,
                    followers.PageSize,
                    followers.TotalCount,
                    followers.TotalPages
                }
            });
        }

        [HttpGet("users/{userID:guid}/following")]
        public async Task<IActionResult> GetUserFollowing(
            Guid userID,
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] string? search = null)
        {
            var userExists = await _context.USERS
                .AsNoTracking()
                .AnyAsync(u => u.user_id == userID);

            if (!userExists)
            {
                return NotFound(new { message = "Không tìm thấy người dùng." });
            }

            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            var viewerID = decodedToken?.userID;
            var normalizedSearch = search?.Trim().ToLower();

            var query = _context.FOLLOWS
                .AsNoTracking()
                .Where(f => f.follower_id == userID);

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                query = query.Where(f =>
                    f.Following.Username.ToLower().Contains(normalizedSearch) ||
                    (f.Following.full_name != null && f.Following.full_name.ToLower().Contains(normalizedSearch)));
            }

            var following = await query
                .OrderByDescending(f => f.created_at)
                .Select(f => new
                {
                    f.created_at,
                    user = new
                    {
                        f.Following.user_id,
                        f.Following.Username,
                        f.Following.full_name,
                        f.Following.avatar_url
                    },
                    is_following = viewerID == null
                        ? (bool?)null
                        : _context.FOLLOWS.Any(viewerFollow =>
                            viewerFollow.follower_id == viewerID.Value &&
                            viewerFollow.following_id == f.Following.user_id)
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(new
            {
                user_id = userID,
                search,
                following,
                pagination = new
                {
                    following.CurrentPage,
                    following.PageSize,
                    following.TotalCount,
                    following.TotalPages
                }
            });
        }

        [HttpGet("follows/status/{userID:guid}")]
        public async Task<IActionResult> GetFollowStatus(Guid userID)
        {
            var targetUserExists = await _context.USERS
                .AsNoTracking()
                .AnyAsync(u => u.user_id == userID);

            if (!targetUserExists)
            {
                return NotFound(new { message = "Không tìm thấy người dùng." });
            }

            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            var viewerID = decodedToken?.userID;

            var isFollowing = viewerID != null && await _context.FOLLOWS
                .AsNoTracking()
                .AnyAsync(f => f.follower_id == viewerID.Value && f.following_id == userID);

            return Ok(new
            {
                user_id = userID,
                viewer_id = viewerID,
                is_authenticated = viewerID != null,
                is_self = viewerID == userID,
                is_following = isFollowing
            });
        }
    }
}
