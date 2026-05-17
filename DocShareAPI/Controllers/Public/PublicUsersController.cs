using DocShareAPI.Data;
using DocShareAPI.Helpers.PageList;
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

        [HttpGet("users/{userID:guid}/documents")]
        public async Task<IActionResult> GetPublicDocumentsByUser(
            Guid userID,
            [FromQuery] PaginationParams paginationParams)
        {
            var user = await _context.USERS
                .AsNoTracking()
                .Where(u => u.user_id == userID)
                .Select(u => new
                {
                    u.user_id,
                    u.Username,
                    u.full_name,
                    u.avatar_url
                })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            var documents = await _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.user_id == userID && d.is_public)
                .OrderByDescending(d => d.uploaded_at)
                .Select(d => new
                {
                    d.document_id,
                    d.user_id,
                    d.Title,
                    d.Description,
                    d.thumbnail_url,
                    d.file_url,
                    d.file_type,
                    d.file_size,
                    d.pages,
                    d.download_count,
                    d.uploaded_at,
                    d.is_public,
                    like_count = d.Likes == null ? 0 : d.Likes.Count(l => l.reaction == 1),
                    dislike_count = d.Likes == null ? 0 : d.Likes.Count(l => l.reaction == -1),
                    categories = d.DocumentCategories.Select(dc => new
                    {
                        dc.Categories.category_id,
                        dc.Categories.Name,
                        dc.Categories.parent_id
                    }),
                    tags = d.DocumentTags!.Select(dt => new
                        {
                            dt.Tags!.tag_id,
                            dt.Tags.Name
                        })
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(new
            {
                user,
                documents,
                pagination = ToPaginationResponse(documents)
            });
        }

        [HttpGet("users/{userID:guid}/collections")]
        public async Task<IActionResult> GetPublicCollectionsByUser(
            Guid userID,
            [FromQuery] PaginationParams paginationParams)
        {
            var user = await _context.USERS
                .AsNoTracking()
                .Where(u => u.user_id == userID)
                .Select(u => new
                {
                    u.user_id,
                    u.Username,
                    u.full_name,
                    u.avatar_url
                })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            var collections = await _context.COLLECTIONS
                .AsNoTracking()
                .Where(c => c.user_id == userID && c.is_public)
                .OrderByDescending(c => c.created_at)
                .Select(c => new
                {
                    c.collection_id,
                    c.user_id,
                    c.Name,
                    c.Description,
                    c.is_public,
                    c.created_at,
                    document_count = c.CollectionDocuments!.Count(cd => cd.Documents != null && cd.Documents.is_public),
                    latest_documents = c.CollectionDocuments!
                            .Where(cd => cd.Documents != null && cd.Documents.is_public)
                            .OrderByDescending(cd => cd.added_at)
                            .Take(4)
                            .Select(cd => new
                            {
                                cd.document_id,
                                cd.added_at,
                                cd.Documents!.Title,
                                cd.Documents.Description,
                                cd.Documents.thumbnail_url,
                                cd.Documents.file_type,
                                cd.Documents.uploaded_at
                            })
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(new
            {
                user,
                collections,
                pagination = ToPaginationResponse(collections)
            });
        }

        [HttpGet("collections/{collectionID:int}")]
        public async Task<IActionResult> GetPublicCollectionById(
            int collectionID,
            [FromQuery] PaginationParams paginationParams)
        {
            var collection = await _context.COLLECTIONS
                .AsNoTracking()
                .Where(c => c.collection_id == collectionID && c.is_public)
                .Select(c => new
                {
                    c.collection_id,
                    c.user_id,
                    c.Name,
                    c.Description,
                    c.is_public,
                    c.created_at,
                    owner = c.Users == null
                        ? null
                        : new
                        {
                            c.Users.user_id,
                            c.Users.Username,
                            c.Users.full_name,
                            c.Users.avatar_url
                        }
                })
                .FirstOrDefaultAsync();

            if (collection == null)
            {
                return NotFound(new { message = "Public collection not found." });
            }

            var documents = await _context.COLLECTION_DOCUMENTS
                .AsNoTracking()
                .Where(cd =>
                    cd.collection_id == collectionID &&
                    cd.Documents != null &&
                    cd.Documents.is_public)
                .OrderByDescending(cd => cd.added_at)
                .Select(cd => new
                {
                    cd.document_id,
                    cd.added_at,
                    cd.Documents!.user_id,
                    cd.Documents.Title,
                    cd.Documents.Description,
                    cd.Documents.thumbnail_url,
                    cd.Documents.file_url,
                    cd.Documents.file_type,
                    cd.Documents.file_size,
                    cd.Documents.pages,
                    cd.Documents.download_count,
                    cd.Documents.uploaded_at,
                    cd.Documents.is_public,
                    owner_name = cd.Documents.Users == null ? null : cd.Documents.Users.full_name,
                    like_count = cd.Documents.Likes == null ? 0 : cd.Documents.Likes.Count(l => l.reaction == 1),
                    dislike_count = cd.Documents.Likes == null ? 0 : cd.Documents.Likes.Count(l => l.reaction == -1),
                    categories = cd.Documents.DocumentCategories.Select(dc => new
                    {
                        dc.Categories.category_id,
                        dc.Categories.Name,
                        dc.Categories.parent_id
                    })
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(new
            {
                collection,
                documents,
                pagination = ToPaginationResponse(documents)
            });
        }

        private static object ToPaginationResponse<T>(PagedList<T> pagedData)
        {
            return new
            {
                pagedData.CurrentPage,
                pagedData.PageSize,
                pagedData.TotalCount,
                pagedData.TotalPages
            };
        }
    }
}
