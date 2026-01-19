using DocShareAPI.Data;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace DocShareAPI.Controllers.Public
{
    [Route("api/public")]
    [ApiController]
    public class PublicDocumentsController : ControllerBase
    {
        private readonly DocShareDbContext _context;

        public PublicDocumentsController(DocShareDbContext context)
        {
            _context = context;
        }

        [HttpGet("document/{documentID}")]
        public async Task<ActionResult> GetDocumentByID(int documentID)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;

            // Lấy document (không filter quyền)
            var document = await _context.DOCUMENTS
                .Where(d => d.document_id == documentID)
                .Select(d => new
                {
                    d.document_id,
                    d.user_id,
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
                return NotFound(new { message = "Document not found." });
            }

            // 2️⃣ Nếu document private → kiểm tra quyền
            if (!document.is_public)
            {
                if (decodedToken == null)
                {
                    return Unauthorized(new { message = "Login required to access this document." });
                }

                bool isOwner = decodedToken.userID == document.user_id;
                bool isAdmin = decodedToken.roleID == "admin";

                if (!isOwner && !isAdmin)
                {
                    return Forbid(); // 403
                }
            }

            return Ok(document);
        }


        //Lấy tài liệu theo search
        [HttpGet("search-documents")]
        public async Task<IActionResult> SearchDocuments([FromQuery] PaginationParams paginationParams, [FromQuery] string search)
        {
            var query = from document in _context.DOCUMENTS
                        join user in _context.USERS on document.user_id equals user.user_id
                        join docCate in _context.DOCUMENT_CATEGORIES on document.document_id equals docCate.document_id into docCateGroup
                        from docCate in docCateGroup.DefaultIfEmpty() // LEFT JOIN

                        join category in _context.CATEGORIES on docCate.category_id equals category.category_id into categoryGroup
                        from category in categoryGroup.DefaultIfEmpty() // LEFT JOIN

                        join docTag in _context.DOCUMENT_TAGS on document.document_id equals docTag.document_id into docTagGroup
                        from docTag in docTagGroup.DefaultIfEmpty() // LEFT JOIN

                        join tag in _context.TAGS on docTag.tag_id equals tag.tag_id into tagGroup
                        from tag in tagGroup.DefaultIfEmpty() // LEFT JOIN

                        select new { document, user, category, tag };

            var documents = await query
                .Where(q => q.document.Title.ToLower().Contains(search.ToLower())
                    || q.category.Name.ToLower().Contains(search.ToLower())
                    || q.tag.Name.ToLower().Contains(search.ToLower())
                    || (q.document.Description != null && q.document.Description.ToLower().Contains(search.ToLower())))
                .Select(q => new
                {
                    q.document.document_id,
                    q.user.full_name,
                    q.document.Title,
                    q.document.thumbnail_url,
                    q.document.is_public,
                })
                .Distinct()
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);
            return Ok(new
            {
                documents = documents,
                Pagination = new
                {
                    documents.CurrentPage,
                    documents.TotalCount,
                    documents.TotalPages
                }
            });
        }
        [HttpGet("documents-by-category")]
        public async Task<ActionResult> GetDocumentsByCategoryId(
    [FromQuery] string categoryID,
    [FromQuery] PaginationParams paginationParams)
        {
            // 1. Kiểm tra category
            var category = await _context.CATEGORIES
                .FirstOrDefaultAsync(c => c.category_id == categoryID);

            if (category == null)
                return NotFound("Không có dữ liệu category hợp lệ");

            // 2. Lấy category cha + con
            var categoryIds = await _context.CATEGORIES
                .Where(c => c.category_id == categoryID || c.parent_id == categoryID)
                .Select(c => c.category_id)
                .ToListAsync();

            // 3. Query document
            var query = from doc in _context.DOCUMENTS
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
                return BadRequest(new { message = "No document IDs provided." });
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
                return BadRequest(new { message = "No valid document IDs provided." });
            }

            // Fetch all matching documents in a single query
            var documents = await _context.DOCUMENTS
                .Where(d => validDocumentIDs.Contains(d.document_id))
                .Include(d => d.Users)
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
