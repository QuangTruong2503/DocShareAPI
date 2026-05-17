using DocShareAPI.Data;
using DocShareAPI.DataTransferObject;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DocShareAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CollectionsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly INotificationService _notificationService;

        public CollectionsController(DocShareDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        // 1. Thêm mới một bộ sưu tập
        [HttpPost("create-collection")]
        public async Task<IActionResult> CreateCollection([FromBody] CollectionDTO collection)
        {
            //Kiểm tra token
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Gán giá trị mặc định nếu cần
            var newCollection = new Collections()
            {
                Name = collection.Name,
                Description = collection.Description,
                created_at = DateTime.UtcNow,
                is_public = collection.is_public,
                user_id = decodedToken.userID
            };
            _context.COLLECTIONS.Add(newCollection);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Tạo bộ sưu tập mới thành công.",
                collection = ToCollectionResponse(newCollection, 0)
            });
        }

        // 2. Cập nhật một bộ sưu tập
        [HttpPut("update/{id}")]
        public async Task<IActionResult> UpdateCollection(int id, [FromBody] CollectionDTO updatedCollection)
        {
            //Kiểm tra token
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var collection = await _context.COLLECTIONS.FindAsync(id);
            if (collection == null)
            {
                return NotFound(new { message = $"Collection with ID {id} not found." });
            }

            if (collection.user_id != decodedToken.userID && decodedToken.roleID != "admin")
            {
                return Forbid();
            }

            // Cập nhật các thuộc tính
            collection.Name = updatedCollection.Name;
            collection.Description = updatedCollection.Description;
            collection.is_public = updatedCollection.is_public;

            await _context.SaveChangesAsync();

            var documentCount = await _context.COLLECTION_DOCUMENTS
                .AsNoTracking()
                .CountAsync(cd => cd.collection_id == id);

            return Ok(new
            {
                success = true,
                message = "Cập nhật bộ sưu tập thành công.",
                collection = ToCollectionResponse(collection, documentCount)
            });
        }

        // 3. Xóa một bộ sưu tập
        [HttpDelete("delete")]
        public async Task<IActionResult> DeleteCollection(int id)
        {
            //Kiểm tra token
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }
            var collection = await _context.COLLECTIONS.FindAsync(id);
            if (collection == null)
            {
                return NotFound(new { message = $"Collection with ID {id} not found." });
            }
            if (collection.user_id != decodedToken.userID && decodedToken.roleID != "admin")
            {
                return Forbid();
            }
            _context.COLLECTIONS.Remove(collection);
            await _context.SaveChangesAsync();

            return Ok($"Xóa thành công bộ sưu tập {collection.Name}");
        }

        // 4. Hiển thị danh sách bộ sưu tập theo người dùng
        [HttpGet("my-collections")]
        public async Task<IActionResult> GetCollectionsByUser()
        {
            //Kiểm tra token
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }
            var collections = await _context.COLLECTIONS
                .AsNoTracking()
                .Where(c => c.user_id == decodedToken.userID)
                .Select(c => new
                {
                    c.user_id,
                    c.collection_id,
                    c.Name,
                    c.Description,
                    c.is_public,
                    c.created_at,
                    DocumentCount = c.CollectionDocuments != null ? c.CollectionDocuments.Count : 0
                }).ToListAsync();

            return Ok(collections);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetCollectionById(int id)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }

            var collection = await _context.COLLECTIONS
                .AsNoTracking()
                .Where(c => c.collection_id == id)
                .Select(c => new
                {
                    c.collection_id,
                    c.user_id,
                    c.Name,
                    c.Description,
                    c.is_public,
                    c.created_at
                })
                .FirstOrDefaultAsync();

            if (collection == null)
            {
                return NotFound(new { message = $"Collection with ID {id} not found." });
            }

            if (collection.user_id != decodedToken.userID && decodedToken.roleID != "admin")
            {
                return Forbid();
            }

            var documents = await _context.COLLECTION_DOCUMENTS
                .AsNoTracking()
                .Where(cd => cd.collection_id == id)
                .OrderByDescending(cd => cd.added_at)
                .Select(cd => new
                {
                    cd.document_id,
                    cd.added_at,
                    cd.Documents!.Title,
                    cd.Documents.Description,
                    cd.Documents.thumbnail_url,
                    cd.Documents.uploaded_at,
                    cd.Documents.is_public,
                    owner_name = cd.Documents.Users != null ? cd.Documents.Users.full_name : null
                })
                .ToListAsync();

            return Ok(new
            {
                collection.collection_id,
                collection.user_id,
                collection.Name,
                collection.Description,
                collection.is_public,
                collection.created_at,
                document_count = documents.Count,
                documents
            });
        }

        [HttpPost("{id}/documents")]
        public async Task<IActionResult> AddDocumentToCollection(int id, [FromBody] CollectionDocumentDTO request)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }

            if (request.document_id <= 0)
            {
                return BadRequest(new { message = "document_id is required." });
            }

            var collection = await _context.COLLECTIONS
                .FirstOrDefaultAsync(c => c.collection_id == id);

            if (collection == null)
            {
                return NotFound(new { message = $"Collection with ID {id} not found." });
            }

            if (collection.user_id != decodedToken.userID && decodedToken.roleID != "admin")
            {
                return Forbid();
            }

            var document = await _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.document_id == request.document_id)
                .Select(d => new
                {
                    d.document_id,
                    d.user_id,
                    d.Title,
                    d.Description,
                    d.thumbnail_url,
                    d.uploaded_at,
                    d.is_public
                })
                .FirstOrDefaultAsync();

            if (document == null)
            {
                return NotFound(new { message = "Document not found." });
            }

            if (!CanAccessDocument(document.user_id, document.is_public, decodedToken))
            {
                return Forbid();
            }

            bool exists = await _context.COLLECTION_DOCUMENTS
                .AnyAsync(cd => cd.collection_id == id && cd.document_id == request.document_id);

            if (exists)
            {
                return Conflict(new { message = "Document already exists in this collection." });
            }

            var collectionDocument = new CollectionDocuments
            {
                collection_id = id,
                document_id = request.document_id,
                added_at = DateTime.UtcNow
            };

            _context.COLLECTION_DOCUMENTS.Add(collectionDocument);
            await _context.SaveChangesAsync();

            if (document.user_id != decodedToken.userID && collection.is_public)
            {
                await _notificationService.CreateAsync(
                    recipientUserId: document.user_id,
                    actorUserId: decodedToken.userID,
                    type: "DOCUMENT_ADDED_TO_COLLECTION",
                    title: "Tài liệu được thêm vào bộ sưu tập",
                    message: $"Tài liệu \"{document.Title}\" đã được thêm vào bộ sưu tập \"{collection.Name}\".",
                    relatedDocumentId: document.document_id,
                    targetUrl: $"/collections/{collection.collection_id}",
                    metadata: new { collection_id = collection.collection_id, collection_name = collection.Name });
            }

            return Ok(new
            {
                success = true,
                message = "Đã lưu tài liệu vào bộ sưu tập.",
                collection_document = new
                {
                    collectionDocument.collection_id,
                    collectionDocument.document_id,
                    collectionDocument.added_at
                },
                document
            });
        }

        [HttpDelete("{id}/documents/{documentId}")]
        public async Task<IActionResult> RemoveDocumentFromCollection(int id, int documentId)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                return Unauthorized();
            }

            var collection = await _context.COLLECTIONS
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.collection_id == id);

            if (collection == null)
            {
                return NotFound(new { message = $"Collection with ID {id} not found." });
            }

            if (collection.user_id != decodedToken.userID && decodedToken.roleID != "admin")
            {
                return Forbid();
            }

            var collectionDocument = await _context.COLLECTION_DOCUMENTS
                .FirstOrDefaultAsync(cd => cd.collection_id == id && cd.document_id == documentId);

            if (collectionDocument == null)
            {
                return NotFound(new { message = "Document is not in this collection." });
            }

            _context.COLLECTION_DOCUMENTS.Remove(collectionDocument);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã xóa tài liệu khỏi bộ sưu tập.",
                collection_id = id,
                document_id = documentId
            });
        }

        private static bool CanAccessDocument(Guid ownerId, bool isPublic, DecodedTokenResponse decodedToken)
        {
            return isPublic || ownerId == decodedToken.userID || decodedToken.roleID == "admin";
        }

        private static object ToCollectionResponse(Collections collection, int? documentCount = null)
        {
            return new
            {
                collection.collection_id,
                collection.user_id,
                collection.Name,
                collection.Description,
                collection.is_public,
                collection.created_at,
                document_count = documentCount
            };
        }
    }
}
