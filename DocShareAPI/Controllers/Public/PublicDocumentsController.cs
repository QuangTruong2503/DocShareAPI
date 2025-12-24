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
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;

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

                    full_name = d.Users.full_name,

                    like_count = d.Likes.Count(l => l.reaction == 1),
                    dislike_count = d.Likes.Count(l => l.reaction == -1),

                    myReaction = decodedTokenResponse == null
                        ? (int?)null
                        : d.Likes
                            .Where(l => l.user_id == decodedTokenResponse.userID)
                            .Select(l => (int?)l.reaction)
                            .SingleOrDefault()
                })
                .AsNoTracking()
                .FirstOrDefaultAsync();


            if (document == null)
            {
                return NotFound(new { message = "Document not found." });
            }

            if (!document.is_public)
            {
                if (decodedTokenResponse != null && (decodedTokenResponse.userID == document.user_id || decodedTokenResponse.roleID == "admin"))
                {
                    return Ok(document);
                }
                return NotFound(new { message = "Không thể truy cập vào tài liệu riêng tư!" });
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
        public async Task<ActionResult> GetDocumentsByCategoryId([FromQuery] string categoryID, [FromQuery] PaginationParams paginationParams)
        {
            var query = from document in _context.DOCUMENTS
                        join user in _context.USERS on document.user_id equals user.user_id
                        join docCate in _context.DOCUMENT_CATEGORIES on document.document_id equals docCate.document_id
                        join cate in _context.CATEGORIES on docCate.category_id equals cate.category_id
                        where docCate.category_id == categoryID || cate.parent_id == categoryID
                        select new
                        {
                            document.document_id,
                            document.Title,
                            user.full_name,
                            document.thumbnail_url,
                            document.is_public,
                            document.uploaded_at,
                            category_name = cate.Name,
                            category_description = cate.Description,
                        };
            var category = await _context.CATEGORIES.FirstOrDefaultAsync(c => c.category_id == categoryID);
            if (category == null)
            {
                return NotFound("Không có dữ liệu category hợp lệ");
            }
            var pagedData = await query.ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(new
            {
                documents = pagedData,
                category_name = category.Name,
                category_description = category.Description,
                Pagination = new
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
                    d.Users.full_name,
                    d.thumbnail_url,
                    d.is_public,
                    d.uploaded_at
                })
                .ToListAsync();

            return Ok(documents);
        }


    }
}
