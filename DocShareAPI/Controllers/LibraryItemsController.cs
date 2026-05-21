using DocShareAPI.Data;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers
{
    [Route("api/library-items")]
    [ApiController]
    public class LibraryItemsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly IFolderPermissionService _permissionService;

        public LibraryItemsController(DocShareDbContext context, IFolderPermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        [HttpPatch("{itemId:int}/rename")]
        public async Task<IActionResult> RenameItem(int itemId, [FromBody] RenameLibraryItemRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var name = request.name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(request.type))
                return BadRequest(Error("VALIDATION_ERROR", "type và name là bắt buộc."));

            var type = request.type.Trim().ToLowerInvariant();
            if (type == "folder")
            {
                var folder = await _context.FOLDERS.FirstOrDefaultAsync(f => f.folder_id == itemId);
                if (folder == null)
                    return NotFound(Error("FOLDER_NOT_FOUND", "Không tìm thấy thư mục."));
                if (!await _permissionService.CanEditFolderAsync(decodedToken.userID, itemId))
                    return Forbid();

                if (await _context.FOLDERS.AnyAsync(f =>
                    f.owner_user_id == folder.owner_user_id &&
                    f.parent_folder_id == folder.parent_folder_id &&
                    f.folder_id != folder.folder_id &&
                    f.name == name))
                {
                    return Conflict(Error("FOLDER_NAME_EXISTS", "Tên thư mục này đã tồn tại."));
                }

                folder.name = name;
                folder.updated_at = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new { item = new { id = folder.folder_id, type = "folder", name = folder.name, updatedAt = folder.updated_at } });
            }

            if (type == "document")
            {
                var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == itemId);
                if (document == null)
                    return NotFound(Error("DOCUMENT_NOT_FOUND", "Không tìm thấy tài liệu."));
                if (document.user_id != decodedToken.userID && decodedToken.roleID != "admin")
                    return Forbid();

                var currentFolderId = await _context.FOLDER_DOCUMENTS
                    .AsNoTracking()
                    .Where(fd => fd.document_id == document.document_id)
                    .Select(fd => (int?)fd.folder_id)
                    .FirstOrDefaultAsync();

                if (await DocumentNameExistsInParent(document.user_id, currentFolderId, name, document.document_id))
                    return Conflict(Error("DOCUMENT_NAME_EXISTS", "Tên tài liệu này đã tồn tại."));

                document.Title = name;
                await _context.SaveChangesAsync();

                return Ok(new { item = new { id = document.document_id, type = "document", name = document.Title, updatedAt = DateTime.UtcNow } });
            }

            return BadRequest(Error("VALIDATION_ERROR", "type chỉ hỗ trợ folder hoặc document."));
        }

        [HttpPatch("move")]
        public async Task<IActionResult> MoveItems([FromBody] MoveLibraryItemsRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            if (request.items == null || request.items.Count == 0)
                return BadRequest(Error("VALIDATION_ERROR", "items là bắt buộc."));

            if (request.targetFolderId.HasValue)
            {
                if (!await _permissionService.CanViewFolderAsync(decodedToken.userID, request.targetFolderId.Value))
                    return NotFound(Error("FOLDER_NOT_FOUND", "Không tìm thấy thư mục đích."));
                if (!await _permissionService.CanAddDocumentToFolderAsync(decodedToken.userID, request.targetFolderId.Value))
                    return Forbid();
            }

            var moved = new List<object>();
            var failed = new List<object>();

            foreach (var item in request.items)
            {
                var type = item.type?.Trim().ToLowerInvariant();
                if (type == "document")
                {
                    await MoveDocument(item.id, request.targetFolderId, decodedToken, moved, failed);
                }
                else if (type == "folder")
                {
                    await MoveFolder(item.id, request.targetFolderId, decodedToken, moved, failed);
                }
                else
                {
                    failed.Add(new { item.id, item.type, code = "VALIDATION_ERROR", message = "type không hợp lệ." });
                }
            }

            await _context.SaveChangesAsync();
            return Ok(new { moved, failed });
        }

        [HttpPost("copy")]
        public IActionResult CopyItems([FromBody] MoveLibraryItemsRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            return Ok(new
            {
                copied = Array.Empty<object>(),
                failed = request.items?.Select(i => new { i.id, i.type, code = "COPY_NOT_ENABLED", message = "Copy chưa được bật trong schema hiện tại." }).ToArray() ?? Array.Empty<object>()
            });
        }

        [HttpPost("/api/folders/merge")]
        public async Task<IActionResult> MergeIntoFolder([FromBody] MergeFolderRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var name = request.name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(Error("VALIDATION_ERROR", "Tên thư mục là bắt buộc."));

            if (request.parentFolderId.HasValue && !await _permissionService.CanAddDocumentToFolderAsync(decodedToken.userID, request.parentFolderId.Value))
                return Forbid();

            if (await _context.FOLDERS.AnyAsync(f => f.owner_user_id == decodedToken.userID && f.parent_folder_id == request.parentFolderId && f.name == name))
                return Conflict(Error("FOLDER_NAME_EXISTS", "Tên thư mục này đã tồn tại."));

            var strategy = _context.Database.CreateExecutionStrategy();
            Folders? folder = null;
            var moved = new List<object>();
            var failed = new List<object>();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                folder = new Folders
                {
                    owner_user_id = decodedToken.userID,
                    parent_folder_id = request.parentFolderId,
                    name = name,
                    description = null,
                    visibility = "private",
                    created_at = DateTime.UtcNow,
                    updated_at = DateTime.UtcNow
                };

                _context.FOLDERS.Add(folder);
                await _context.SaveChangesAsync();

                foreach (var item in request.items ?? new List<LibraryItemRef>())
                {
                    if (string.Equals(item.type, "document", StringComparison.OrdinalIgnoreCase))
                        await MoveDocument(item.id, folder.folder_id, decodedToken, moved, failed);
                    else
                        failed.Add(new { item.id, item.type, code = "VALIDATION_ERROR", message = "Merge hiện chỉ hỗ trợ document." });
                }

                if (failed.Count > 0)
                    throw new InvalidOperationException("MERGE_MOVE_FAILED");

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            });

            return Ok(new
            {
                folder = new { id = folder!.folder_id, type = "folder", name = folder.name, parentFolderId = folder.parent_folder_id },
                moved
            });
        }

        [HttpPatch("trash")]
        public IActionResult TrashItems([FromBody] MoveLibraryItemsRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            return Ok(new
            {
                trashed = Array.Empty<object>(),
                failed = request.items?.Select(i => new { i.id, i.type, code = "TRASH_NOT_ENABLED", message = "Trash chưa được bật trong schema hiện tại." }).ToArray() ?? Array.Empty<object>()
            });
        }

        [HttpPatch("restore")]
        public IActionResult RestoreItems([FromBody] MoveLibraryItemsRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            return Ok(new
            {
                restored = Array.Empty<object>(),
                failed = request.items?.Select(i => new { i.id, i.type, code = "TRASH_NOT_ENABLED", message = "Trash chưa được bật trong schema hiện tại." }).ToArray() ?? Array.Empty<object>()
            });
        }

        [HttpDelete]
        public IActionResult DeleteForever([FromBody] MoveLibraryItemsRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            return Ok(new
            {
                deleted = Array.Empty<object>(),
                failed = request.items?.Select(i => new { i.id, i.type, code = "TRASH_NOT_ENABLED", message = "Delete forever yêu cầu trash schema." }).ToArray() ?? Array.Empty<object>()
            });
        }

        [HttpPut("{itemId:int}/favorite")]
        public IActionResult FavoriteItem(int itemId, [FromBody] FavoriteLibraryItemRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            return Ok(new
            {
                id = itemId,
                type = request.type,
                isFavorite = false,
                message = "Favorites are not enabled in the current database schema."
            });
        }

        private async Task MoveDocument(int documentId, int? targetFolderId, DecodedTokenResponse decodedToken, List<object> moved, List<object> failed)
        {
            var document = await _context.DOCUMENTS.AsNoTracking().FirstOrDefaultAsync(d => d.document_id == documentId);
            if (document == null)
            {
                failed.Add(new { id = documentId, type = "document", code = "DOCUMENT_NOT_FOUND", message = "Không tìm thấy tài liệu." });
                return;
            }
            if (document.user_id != decodedToken.userID && decodedToken.roleID != "admin")
            {
                failed.Add(new { id = documentId, type = "document", code = "FORBIDDEN", message = "Không có quyền di chuyển tài liệu." });
                return;
            }
            if (await DocumentNameExistsInParent(document.user_id, targetFolderId, document.Title, document.document_id))
            {
                failed.Add(new { id = documentId, type = "document", code = "NAME_CONFLICT", message = "Tên tài liệu đã tồn tại ở thư mục đích." });
                return;
            }

            var link = await _context.FOLDER_DOCUMENTS.FirstOrDefaultAsync(fd => fd.document_id == documentId);
            if (targetFolderId.HasValue)
            {
                if (link == null)
                    _context.FOLDER_DOCUMENTS.Add(new FolderDocuments { document_id = documentId, folder_id = targetFolderId.Value, added_by_user_id = decodedToken.userID, added_at = DateTime.UtcNow });
                else
                    link.folder_id = targetFolderId.Value;
            }
            else if (link != null)
            {
                _context.FOLDER_DOCUMENTS.Remove(link);
            }

            moved.Add(new { id = documentId, type = "document", parentFolderId = targetFolderId });
        }

        private async Task MoveFolder(int folderId, int? targetFolderId, DecodedTokenResponse decodedToken, List<object> moved, List<object> failed)
        {
            var folder = await _context.FOLDERS.FirstOrDefaultAsync(f => f.folder_id == folderId);
            if (folder == null)
            {
                failed.Add(new { id = folderId, type = "folder", code = "FOLDER_NOT_FOUND", message = "Không tìm thấy thư mục." });
                return;
            }
            if (!await _permissionService.CanEditFolderAsync(decodedToken.userID, folderId))
            {
                failed.Add(new { id = folderId, type = "folder", code = "FORBIDDEN", message = "Không có quyền di chuyển thư mục." });
                return;
            }
            if (targetFolderId == folderId)
            {
                failed.Add(new { id = folderId, type = "folder", code = "CANNOT_MOVE_FOLDER_INTO_ITSELF", message = "Không thể di chuyển thư mục vào chính nó." });
                return;
            }
            if (targetFolderId.HasValue && await IsDescendant(folderId, targetFolderId.Value))
            {
                failed.Add(new { id = folderId, type = "folder", code = "CANNOT_MOVE_FOLDER_INTO_DESCENDANT", message = "Không thể di chuyển thư mục vào thư mục con của nó." });
                return;
            }
            if (await _context.FOLDERS.AnyAsync(f => f.owner_user_id == folder.owner_user_id && f.parent_folder_id == targetFolderId && f.folder_id != folderId && f.name == folder.name))
            {
                failed.Add(new { id = folderId, type = "folder", code = "NAME_CONFLICT", message = "Tên thư mục đã tồn tại ở thư mục đích." });
                return;
            }

            folder.parent_folder_id = targetFolderId;
            folder.updated_at = DateTime.UtcNow;
            moved.Add(new { id = folderId, type = "folder", parentFolderId = targetFolderId });
        }

        private async Task<bool> DocumentNameExistsInParent(Guid ownerUserId, int? parentFolderId, string name, int exceptDocumentId)
        {
            if (parentFolderId.HasValue)
            {
                return await _context.FOLDER_DOCUMENTS.AnyAsync(fd =>
                    fd.folder_id == parentFolderId.Value &&
                    fd.document_id != exceptDocumentId &&
                    fd.Document != null &&
                    fd.Document.Title == name);
            }

            return await _context.DOCUMENTS.AnyAsync(d =>
                d.user_id == ownerUserId &&
                d.document_id != exceptDocumentId &&
                d.Title == name &&
                !_context.FOLDER_DOCUMENTS.Any(fd => fd.document_id == d.document_id));
        }

        private async Task<bool> IsDescendant(int sourceFolderId, int targetFolderId)
        {
            var current = await _context.FOLDERS.AsNoTracking().FirstOrDefaultAsync(f => f.folder_id == targetFolderId);
            while (current?.parent_folder_id != null)
            {
                if (current.parent_folder_id.Value == sourceFolderId)
                    return true;
                current = await _context.FOLDERS.AsNoTracking().FirstOrDefaultAsync(f => f.folder_id == current.parent_folder_id.Value);
            }
            return false;
        }

        private static object Error(string code, string message, object? details = null)
        {
            return new { success = false, code, message, details };
        }
    }

    public class RenameLibraryItemRequest
    {
        public string? type { get; set; }
        public string? name { get; set; }
    }

    public class LibraryItemRef
    {
        public int id { get; set; }
        public string? type { get; set; }
    }

    public class MoveLibraryItemsRequest
    {
        public List<LibraryItemRef>? items { get; set; }
        public int? targetFolderId { get; set; }
        public string? conflictStrategy { get; set; }
    }

    public class MergeFolderRequest
    {
        public string? name { get; set; }
        public int? parentFolderId { get; set; }
        public List<LibraryItemRef>? items { get; set; }
    }

    public class FavoriteLibraryItemRequest
    {
        public string? type { get; set; }
        public bool favorite { get; set; }
    }
}
