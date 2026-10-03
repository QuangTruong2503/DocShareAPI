using DocShareAPI.Data;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace DocShareAPI.Controllers.Public
{
    [Route("api/public")]
    [ApiController]
    public class PublicDocumentsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly IFolderPermissionService _folderPermissionService;

        public PublicDocumentsController(
            DocShareDbContext context,
            IFolderPermissionService folderPermissionService)
        {
            _context = context;
            _folderPermissionService = folderPermissionService;
        }

        [HttpGet("document/{documentID}")]
        public async Task<ActionResult> GetDocumentByID(int documentID)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;

            // Lấy document (không filter quyền)
            var document = await _context.DOCUMENTS.Where(d => d.deleted_at == null)
                .AsNoTracking()
                .Where(d => d.document_id == documentID)
                .Select(d => new
                {
                    d.document_id,
                    d.user_id,
                    full_name = d.Users != null ? d.Users.full_name : null,
                    d.Title,
                    d.Description,
                    d.file_url,
                    d.is_public,
                    d.download_count,
                    d.file_size,
                    d.file_type,
                    d.uploaded_at,

                    like_count = d.Likes != null ? d.Likes.Count(l => l.reaction == 1) : 0,
                    dislike_count = d.Likes != null ? d.Likes.Count(l => l.reaction == -1) : 0,

                    myReaction = decodedToken == null
                        ? (int?)null
                        : (d.Likes != null
                            ? d.Likes
                                .Where(l => l.user_id == decodedToken.userID)
                                .Select(l => (int?)l.reaction)
                                .FirstOrDefault()
                            : null),

                    categories = d.DocumentCategories
                        .Select(dc => new
                        {
                            dc.Categories.category_id,
                            dc.Categories.Name,
                            dc.Categories.parent_id
                        })
                })
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (document == null)
            {
                return NotFound(new { message = "Không tìm thấy tài liệu." });
            }

            var folderAccess = await GetFolderAccessAsync(document.document_id, decodedToken);

            // Nếu document private thì vẫn cho xem khi người dùng xem được folder chứa tài liệu.
            if (!document.is_public)
            {
                if (decodedToken == null && !folderAccess.CanView)
                {
                    return Unauthorized(new { message = "Bạn cần đăng nhập để truy cập tài liệu này." });
                }

                bool isOwner = decodedToken != null && decodedToken.userID == document.user_id;
                bool isAdmin = string.Equals(decodedToken?.roleID, "admin", StringComparison.OrdinalIgnoreCase);

                if (!isOwner && !isAdmin && !folderAccess.CanView)
                {
                    return Forbid(); // 403
                }
            }

            return Ok(new
            {
                document.document_id,
                document.user_id,
                document.full_name,
                document.Title,
                document.Description,
                document.file_url,
                document.is_public,
                document.download_count,
                document.file_size,
                document.file_type,
                document.uploaded_at,
                document.like_count,
                document.dislike_count,
                document.myReaction,
                document.categories,
                parent_folder_id = folderAccess.FolderId,
                folder_visibility = folderAccess.Visibility,
                access_source = document.is_public ? "public_document" : folderAccess.CanView ? "folder" : "owner_or_admin"
            });
        }

        private async Task<FolderAccess> GetFolderAccessAsync(int documentId, DecodedTokenResponse? decodedToken)
        {
            var folder = await _context.FOLDER_DOCUMENTS
                .AsNoTracking()
                .Where(fd => fd.document_id == documentId)
                .Select(fd => new
                {
                    fd.folder_id,
                    fd.Folder!.visibility
                })
                .FirstOrDefaultAsync();

            if (folder == null)
            {
                return new FolderAccess(null, null, false);
            }

            var canView = await _folderPermissionService.CanViewFolderAsync(decodedToken?.userID, folder.folder_id);
            return new FolderAccess(folder.folder_id, folder.visibility, canView);
        }

        private sealed record FolderAccess(int? FolderId, string? Visibility, bool CanView);


        //Lấy tài liệu theo search
        [HttpGet("search-documents")]
        public async Task<IActionResult> SearchDocuments(
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] string search,
            [FromQuery] string sortBy = "relevance")
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return BadRequest(new { message = "Từ khóa tìm kiếm là bắt buộc." });
            }

            var normalizedSearch = NormalizeSearchInput(search);
            var phrasePattern = ToContainsPattern(normalizedSearch);
            var prefixPattern = ToPrefixPattern(normalizedSearch);
            var terms = GetSearchTerms(normalizedSearch);

            var query = _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.is_public && d.deleted_at == null);

            foreach (var term in terms)
            {
                var termPattern = ToContainsPattern(term);
                query = query.Where(d =>
                    EF.Functions.Like(d.Title.ToLower(), termPattern) ||
                    (d.Description != null && EF.Functions.Like(d.Description.ToLower(), termPattern)) ||
                    (d.Users != null && EF.Functions.Like(d.Users.Username.ToLower(), termPattern)) ||
                    (d.Users != null && d.Users.full_name != null && EF.Functions.Like(d.Users.full_name.ToLower(), termPattern)) ||
                    _context.DOCUMENT_CATEGORIES.Any(dc =>
                        dc.document_id == d.document_id &&
                        _context.CATEGORIES.Any(c =>
                            c.category_id == dc.category_id &&
                            (EF.Functions.Like(c.Name.ToLower(), termPattern) ||
                             EF.Functions.Like(c.category_id.ToLower(), termPattern)))) ||
                    _context.DOCUMENT_TAGS.Any(dt =>
                        dt.document_id == d.document_id &&
                        _context.TAGS.Any(t =>
                            t.tag_id == dt.tag_id &&
                            (EF.Functions.Like(t.Name.ToLower(), termPattern) ||
                             EF.Functions.Like(t.tag_id.ToLower(), termPattern)))));
            }

            var scoredQuery = query.Select(d => new
            {
                d.document_id,
                full_name = d.Users != null ? d.Users.full_name : null,
                d.Title,
                d.Description,
                d.thumbnail_url,
                d.is_public,
                d.file_type,
                d.download_count,
                d.uploaded_at,
                searchScore =
                    (d.Title.ToLower() == normalizedSearch ? 1000 : 0) +
                    (EF.Functions.Like(d.Title.ToLower(), prefixPattern) ? 700 : 0) +
                    (EF.Functions.Like(d.Title.ToLower(), phrasePattern) ? 500 : 0) +
                    (_context.DOCUMENT_CATEGORIES.Any(dc =>
                        dc.document_id == d.document_id &&
                        _context.CATEGORIES.Any(c =>
                            c.category_id == dc.category_id &&
                            EF.Functions.Like(c.Name.ToLower(), phrasePattern))) ? 300 : 0) +
                    (_context.DOCUMENT_TAGS.Any(dt =>
                        dt.document_id == d.document_id &&
                        _context.TAGS.Any(t =>
                            t.tag_id == dt.tag_id &&
                            EF.Functions.Like(t.Name.ToLower(), phrasePattern))) ? 250 : 0) +
                    (d.Description != null && EF.Functions.Like(d.Description.ToLower(), phrasePattern) ? 150 : 0) +
                    (d.Users != null && d.Users.full_name != null && EF.Functions.Like(d.Users.full_name.ToLower(), phrasePattern) ? 100 : 0) +
                    (d.Users != null && EF.Functions.Like(d.Users.Username.ToLower(), phrasePattern) ? 100 : 0)
            });

            scoredQuery = sortBy.Trim().ToLowerInvariant() switch
            {
                "date" or "newest" => scoredQuery
                    .OrderByDescending(d => d.uploaded_at)
                    .ThenByDescending(d => d.searchScore),
                "downloads" or "popular" => scoredQuery
                    .OrderByDescending(d => d.download_count)
                    .ThenByDescending(d => d.searchScore),
                "title" => scoredQuery
                    .OrderBy(d => d.Title)
                    .ThenByDescending(d => d.searchScore),
                _ => scoredQuery
                    .OrderByDescending(d => d.searchScore)
                    .ThenByDescending(d => d.download_count)
                    .ThenByDescending(d => d.uploaded_at)
            };

            var documents = await scoredQuery
                .Select(d => new
                {
                    d.document_id,
                    d.full_name,
                    d.Title,
                    d.Description,
                    d.thumbnail_url,
                    d.is_public,
                    d.file_type,
                    d.download_count,
                    d.uploaded_at,
                    d.searchScore
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(new
            {
                documents = documents,
                search = new
                {
                    query = search,
                    normalized = normalizedSearch,
                    terms,
                    sortBy
                },
                Pagination = new
                {
                    documents.CurrentPage,
                    documents.TotalCount,
                    documents.TotalPages
                }
            });
        }

        private static string NormalizeSearchInput(string search)
        {
            return Regex.Replace(search.Trim(), @"\s+", " ").ToLowerInvariant();
        }

        private static IReadOnlyList<string> GetSearchTerms(string normalizedSearch)
        {
            return Regex.Matches(normalizedSearch, @"[\p{L}\p{N}]+")
                .Select(match => match.Value)
                .Where(term => term.Length > 1)
                .Distinct()
                .Take(8)
                .DefaultIfEmpty(normalizedSearch)
                .ToArray();
        }

        private static string ToContainsPattern(string value)
        {
            return $"%{value}%";
        }

        private static string ToPrefixPattern(string value)
        {
            return $"{value}%";
        }
        [HttpGet("documents-by-category")]
        public async Task<ActionResult> GetDocumentsByCategoryId(
    [FromQuery] string categoryID,
    [FromQuery] PaginationParams paginationParams)
        {
            // 1. Kiểm tra category
            var category = await _context.CATEGORIES
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.category_id == categoryID);

            if (category == null)
                return NotFound("Không có dữ liệu category hợp lệ");

            // 2. Lấy category cha + con
            var categoryIds = await _context.CATEGORIES
                .AsNoTracking()
                .Where(c => c.category_id == categoryID || c.parent_id == categoryID)
                .Select(c => c.category_id)
                .ToListAsync();

            // 3. Query document
            var query = from doc in _context.DOCUMENTS.Where(d => d.deleted_at == null)
                        join user in _context.USERS on doc.user_id equals user.user_id
                        join dc in _context.DOCUMENT_CATEGORIES on doc.document_id equals dc.document_id
                        where categoryIds.Contains(dc.category_id)
                              && doc.is_public
                        select new
                        {
                            doc.document_id,
                            doc.Title,
                            user.full_name,
                            doc.thumbnail_url,
                            doc.uploaded_at
                        };

            var pagedData = await query
                .Distinct()
                .OrderByDescending(d => d.uploaded_at)
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(new
            {
                category_id = category.category_id,
                category_name = category.Name,
                category_description = category.Description,
                documents = pagedData,
                pagination = new
                {
                    pagedData.CurrentPage,
                    pagedData.PageSize,
                    pagedData.TotalCount,
                    pagedData.TotalPages
                }
            });
        }


        //Lấy dữ liệu tài liệu theo lịch sử xem
        [HttpPost("history-documents")]
        public async Task<ActionResult> GetHistoryDocuments([FromBody] List<string> documentIDs)
        {
            // Validate input
            if (documentIDs == null || !documentIDs.Any())
            {
                return BadRequest(new { message = "Chưa cung cấp ID tài liệu nào." });
            }

            // Convert documentIDs to integers, handling invalid IDs
            var validDocumentIDs = new List<int>();
            foreach (var id in documentIDs)
            {
                if (int.TryParse(id, out int parsedID))
                {
                    validDocumentIDs.Add(parsedID);
                }
                // Optionally log or return invalid IDs if needed
            }

            if (!validDocumentIDs.Any())
            {
                return BadRequest(new { message = "Không có ID tài liệu hợp lệ nào được cung cấp." });
            }

            // Fetch all matching documents in a single query
            var documents = await _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => validDocumentIDs.Contains(d.document_id) && d.is_public == true)
                .Select(d => new // Use a DTO for type safety
                {
                    d.document_id,
                    d.Title,
                    full_name = d.Users != null ? d.Users.full_name : null,
                    d.thumbnail_url,
                    d.is_public,
                    d.uploaded_at
                })
                .ToListAsync();

            return Ok(documents);
        }


    }
}
