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
                var folder = await _context.FOLDERS.FirstOrDefaultAsync(f => f.folder_id == itemId && f.deleted_at == null);
                if (folder == null)
                    return NotFound(Error("FOLDER_NOT_FOUND", "Không tìm thấy thư mục."));
                if (!await _permissionService.CanEditFolderAsync(decodedToken.userID, itemId))
                    return Forbid();

                if (await _context.FOLDERS.AnyAsync(f =>
                    f.owner_user_id == folder.owner_user_id &&
                    f.parent_folder_id == folder.parent_folder_id &&
                    f.deleted_at == null &&
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
                var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == itemId && d.deleted_at == null);
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
                if (!await _context.FOLDERS.AnyAsync(f => f.folder_id == request.targetFolderId.Value && f.deleted_at == null) ||
                    !await _permissionService.CanViewFolderAsync(decodedToken.userID, request.targetFolderId.Value))
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

            if (await _context.FOLDERS.AnyAsync(f => f.owner_user_id == decodedToken.userID && f.parent_folder_id == request.parentFolderId && f.name == name && f.deleted_at == null))
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
        public async Task<IActionResult> TrashItems([FromBody] MoveLibraryItemsRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));
            if (!IsValidBatchRequest(request))
                return BadRequest(Error("INVALID_PAYLOAD", "Payload phải có items và type là document hoặc folder."));

            var trashed = new List<object>();
            var failed = new List<object>();
            var strategy = _context.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                foreach (var item in request.items!)
                {
                    var type = NormalizeType(item.type);
                    if (type == "document")
                        await TrashDocument(item.id, decodedToken, trashed, failed);
                    else if (type == "folder")
                        await TrashFolder(item.id, decodedToken, trashed, failed);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            });

            return Ok(new { success = failed.Count == 0, trashed, failed });
        }

        [HttpPatch("restore")]
        public async Task<IActionResult> RestoreItems([FromBody] MoveLibraryItemsRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));
            if (!IsValidBatchRequest(request))
                return BadRequest(Error("INVALID_PAYLOAD", "Payload phải có items và type là document hoặc folder."));

            var restored = new List<object>();
            var failed = new List<object>();
            var strategy = _context.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                foreach (var item in request.items!)
                {
                    var type = NormalizeType(item.type);
                    if (type == "document")
                        await RestoreDocument(item.id, decodedToken, restored, failed);
                    else if (type == "folder")
                        await RestoreFolder(item.id, decodedToken, restored, failed);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            });

            return Ok(new { success = failed.Count == 0, restored, failed });
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteForever([FromBody] MoveLibraryItemsRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));
            if (!IsValidBatchRequest(request))
                return BadRequest(Error("INVALID_PAYLOAD", "Payload phải có items và type là document hoặc folder."));

            var deleted = new List<object>();
            var failed = new List<object>();
            var strategy = _context.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                foreach (var item in request.items!)
                {
                    var type = NormalizeType(item.type);
                    if (type == "document")
                        await DeleteDocumentForever(item.id, decodedToken, deleted, failed);
                    else if (type == "folder")
                        await DeleteFolderForever(item.id, decodedToken, deleted, failed);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            });

            return Ok(new { success = failed.Count == 0, deleted, failed });
        }

        private async Task TrashDocument(int documentId, DecodedTokenResponse decodedToken, List<object> trashed, List<object> failed)
        {
            var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentId);
            if (document == null)
            {
                failed.Add(new { id = documentId, type = "document", code = "DOCUMENT_NOT_FOUND", message = "Không tìm thấy tài liệu." });
                return;
            }
            if (!CanDeleteDocument(document, decodedToken))
            {
                failed.Add(new { id = documentId, type = "document", code = "FORBIDDEN", message = "Bạn không có quyền xóa tài liệu này." });
                return;
            }

            if (document.deleted_at != null)
            {
                trashed.Add(new { id = documentId, type = "document", trashedAt = document.deleted_at });
                return;
            }

            var parentFolderId = await GetDocumentParentFolderId(documentId);
            var now = DateTime.UtcNow;
            document.deleted_at = now;
            document.deleted_by = decodedToken.userID;
            document.deleted_root_type = "document";
            document.deleted_root_id = document.document_id;
            document.original_parent_folder_id = parentFolderId;
            trashed.Add(new { id = documentId, type = "document", trashedAt = now });
        }

        private async Task TrashFolder(int folderId, DecodedTokenResponse decodedToken, List<object> trashed, List<object> failed)
        {
            var folder = await _context.FOLDERS.FirstOrDefaultAsync(f => f.folder_id == folderId);
            if (folder == null)
            {
                failed.Add(new { id = folderId, type = "folder", code = "FOLDER_NOT_FOUND", message = "Không tìm thấy thư mục." });
                return;
            }
            if (!await _permissionService.CanDeleteFolderAsync(decodedToken.userID, folderId))
            {
                failed.Add(new { id = folderId, type = "folder", code = "FORBIDDEN", message = "Bạn không có quyền xóa thư mục này." });
                return;
            }
            if (folder.deleted_at != null)
            {
                trashed.Add(new { id = folderId, type = "folder", trashedAt = folder.deleted_at });
                return;
            }

            var now = DateTime.UtcNow;
            var folderIds = await GetFolderSubtreeIds(folderId);
            var folders = await _context.FOLDERS.Where(f => folderIds.Contains(f.folder_id)).ToListAsync();
            var documentIds = await _context.FOLDER_DOCUMENTS
                .Where(fd => folderIds.Contains(fd.folder_id))
                .Select(fd => fd.document_id)
                .Distinct()
                .ToListAsync();
            var documents = await _context.DOCUMENTS
                .Where(d => documentIds.Contains(d.document_id) && d.deleted_at == null)
                .ToListAsync();
            var documentParentIds = await _context.FOLDER_DOCUMENTS
                .Where(fd => documentIds.Contains(fd.document_id))
                .ToDictionaryAsync(fd => fd.document_id, fd => (int?)fd.folder_id);

            foreach (var childFolder in folders)
            {
                childFolder.deleted_at = now;
                childFolder.deleted_by = decodedToken.userID;
                childFolder.deleted_root_type = "folder";
                childFolder.deleted_root_id = folderId;
                childFolder.original_parent_folder_id = childFolder.parent_folder_id;
                childFolder.updated_at = now;
            }

            foreach (var document in documents)
            {
                document.deleted_at = now;
                document.deleted_by = decodedToken.userID;
                document.deleted_root_type = "folder";
                document.deleted_root_id = folderId;
                document.original_parent_folder_id = documentParentIds.TryGetValue(document.document_id, out var parentId) ? parentId : null;
            }

            trashed.Add(new { id = folderId, type = "folder", trashedAt = now });
        }

        private async Task RestoreDocument(int documentId, DecodedTokenResponse decodedToken, List<object> restored, List<object> failed)
        {
            var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentId);
            if (document == null)
            {
                failed.Add(new { id = documentId, type = "document", code = "DOCUMENT_NOT_FOUND", message = "Không tìm thấy tài liệu." });
                return;
            }
            if (!CanDeleteDocument(document, decodedToken))
            {
                failed.Add(new { id = documentId, type = "document", code = "FORBIDDEN", message = "Bạn không có quyền khôi phục tài liệu này." });
                return;
            }
            if (document.deleted_at == null)
            {
                failed.Add(new { id = documentId, type = "document", code = "NOT_TRASHED", message = "Tài liệu chưa nằm trong thùng rác." });
                return;
            }

            var parentFolderId = await ResolveRestoreParent(document.original_parent_folder_id, decodedToken.userID);
            document.Title = await UniqueDocumentName(document.user_id, parentFolderId, document.Title, document.document_id);
            await MoveDocumentLink(document.document_id, parentFolderId, decodedToken.userID);
            ClearDocumentTrash(document);
            restored.Add(new { id = documentId, type = "document", parentFolderId });
        }

        private async Task RestoreFolder(int folderId, DecodedTokenResponse decodedToken, List<object> restored, List<object> failed)
        {
            var folder = await _context.FOLDERS.FirstOrDefaultAsync(f => f.folder_id == folderId);
            if (folder == null)
            {
                failed.Add(new { id = folderId, type = "folder", code = "FOLDER_NOT_FOUND", message = "Không tìm thấy thư mục." });
                return;
            }
            if (folder.owner_user_id != decodedToken.userID && decodedToken.roleID != "admin")
            {
                failed.Add(new { id = folderId, type = "folder", code = "FORBIDDEN", message = "Bạn không có quyền khôi phục thư mục này." });
                return;
            }
            if (folder.deleted_at == null || folder.deleted_root_type != "folder" || folder.deleted_root_id != folderId)
            {
                failed.Add(new { id = folderId, type = "folder", code = "NOT_TRASHED", message = "Thư mục chưa nằm trong thùng rác hoặc không phải thư mục gốc đã xóa." });
                return;
            }

            var parentFolderId = await ResolveRestoreParent(folder.original_parent_folder_id, decodedToken.userID);
            folder.name = await UniqueFolderName(folder.owner_user_id, parentFolderId, folder.name, folder.folder_id);
            folder.parent_folder_id = parentFolderId;

            var folders = await _context.FOLDERS
                .Where(f => f.deleted_root_type == "folder" && f.deleted_root_id == folderId)
                .ToListAsync();
            var documents = await _context.DOCUMENTS
                .Where(d => d.deleted_root_type == "folder" && d.deleted_root_id == folderId)
                .ToListAsync();

            foreach (var childFolder in folders)
                ClearFolderTrash(childFolder);
            foreach (var document in documents)
                ClearDocumentTrash(document);

            restored.Add(new { id = folderId, type = "folder", parentFolderId });
        }

        private async Task DeleteDocumentForever(int documentId, DecodedTokenResponse decodedToken, List<object> deleted, List<object> failed)
        {
            var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentId);
            if (document == null)
            {
                failed.Add(new { id = documentId, type = "document", code = "DOCUMENT_NOT_FOUND", message = "Không tìm thấy tài liệu." });
                return;
            }
            if (!CanDeleteDocument(document, decodedToken))
            {
                failed.Add(new { id = documentId, type = "document", code = "FORBIDDEN", message = "Bạn không có quyền xóa vĩnh viễn tài liệu này." });
                return;
            }
            if (document.deleted_at == null)
            {
                failed.Add(new { id = documentId, type = "document", code = "NOT_TRASHED", message = "Tài liệu chưa nằm trong thùng rác." });
                return;
            }

            await DeleteDocumentDependencies(new[] { documentId });
            _context.DOCUMENTS.Remove(document);
            deleted.Add(new { id = documentId, type = "document" });
        }

        private async Task DeleteFolderForever(int folderId, DecodedTokenResponse decodedToken, List<object> deleted, List<object> failed)
        {
            var folder = await _context.FOLDERS.FirstOrDefaultAsync(f => f.folder_id == folderId);
            if (folder == null)
            {
                failed.Add(new { id = folderId, type = "folder", code = "FOLDER_NOT_FOUND", message = "Không tìm thấy thư mục." });
                return;
            }
            if (folder.owner_user_id != decodedToken.userID && decodedToken.roleID != "admin")
            {
                failed.Add(new { id = folderId, type = "folder", code = "FORBIDDEN", message = "Bạn không có quyền xóa vĩnh viễn thư mục này." });
                return;
            }
            if (folder.deleted_at == null || folder.deleted_root_type != "folder" || folder.deleted_root_id != folderId)
            {
                failed.Add(new { id = folderId, type = "folder", code = "NOT_TRASHED", message = "Thư mục chưa nằm trong thùng rác hoặc không phải thư mục gốc đã xóa." });
                return;
            }

            var folderIds = await _context.FOLDERS
                .Where(f => f.deleted_root_type == "folder" && f.deleted_root_id == folderId)
                .Select(f => f.folder_id)
                .ToListAsync();
            var documentIds = await _context.DOCUMENTS
                .Where(d => d.deleted_root_type == "folder" && d.deleted_root_id == folderId)
                .Select(d => d.document_id)
                .ToListAsync();

            await DeleteDocumentDependencies(documentIds);
            await _context.FOLDER_INVITES.Where(i => folderIds.Contains(i.folder_id)).ExecuteDeleteAsync();
            await _context.FOLDER_MEMBERS.Where(m => folderIds.Contains(m.folder_id)).ExecuteDeleteAsync();
            await _context.FOLDER_DOCUMENTS.Where(fd => folderIds.Contains(fd.folder_id)).ExecuteDeleteAsync();
            await _context.NOTIFICATIONS.Where(n => n.related_folder_id.HasValue && folderIds.Contains(n.related_folder_id.Value)).ExecuteUpdateAsync(s => s.SetProperty(n => n.related_folder_id, (int?)null));

            var documents = await _context.DOCUMENTS.Where(d => documentIds.Contains(d.document_id)).ToListAsync();
            _context.DOCUMENTS.RemoveRange(documents);
            var folders = await _context.FOLDERS.Where(f => folderIds.Contains(f.folder_id)).ToListAsync();
            var folderDepth = folders.ToDictionary(f => f.folder_id, f => FolderDepth(f, folders));
            _context.FOLDERS.RemoveRange(folders.OrderByDescending(f => folderDepth[f.folder_id]));
            deleted.Add(new { id = folderId, type = "folder" });
        }

        private async Task DeleteDocumentDependencies(IEnumerable<int> documentIds)
        {
            var ids = documentIds.Distinct().ToList();
            if (ids.Count == 0)
                return;

            await _context.FOLDER_DOCUMENTS.Where(fd => ids.Contains(fd.document_id)).ExecuteDeleteAsync();
            await _context.COLLECTION_DOCUMENTS.Where(cd => ids.Contains(cd.document_id)).ExecuteDeleteAsync();
            await _context.DOCUMENT_TAGS.Where(dt => ids.Contains(dt.document_id)).ExecuteDeleteAsync();
            await _context.DOCUMENT_CATEGORIES.Where(dc => ids.Contains(dc.document_id)).ExecuteDeleteAsync();
            await _context.LIKES.Where(l => ids.Contains(l.document_id)).ExecuteDeleteAsync();
            await _context.REPORTS.Where(r => ids.Contains(r.document_id)).ExecuteDeleteAsync();
            await _context.NOTIFICATIONS.Where(n => n.related_document_id.HasValue && ids.Contains(n.related_document_id.Value)).ExecuteUpdateAsync(s => s.SetProperty(n => n.related_document_id, (int?)null));
        }

        private async Task<List<int>> GetFolderSubtreeIds(int rootFolderId)
        {
            var folders = await _context.FOLDERS.AsNoTracking().Select(f => new { f.folder_id, f.parent_folder_id }).ToListAsync();
            var result = new List<int> { rootFolderId };
            for (var index = 0; index < result.Count; index++)
            {
                var currentId = result[index];
                result.AddRange(folders.Where(f => f.parent_folder_id == currentId).Select(f => f.folder_id));
            }
            return result.Distinct().ToList();
        }

        private async Task<int?> ResolveRestoreParent(int? originalParentFolderId, Guid userId)
        {
            if (!originalParentFolderId.HasValue)
                return null;

            var parentExists = await _context.FOLDERS.AnyAsync(f =>
                f.folder_id == originalParentFolderId.Value &&
                f.deleted_at == null &&
                (f.owner_user_id == userId || f.FolderMembers.Any(m => m.user_id == userId)));

            return parentExists ? originalParentFolderId.Value : null;
        }

        private async Task MoveDocumentLink(int documentId, int? parentFolderId, Guid actorUserId)
        {
            var link = await _context.FOLDER_DOCUMENTS.FirstOrDefaultAsync(fd => fd.document_id == documentId);
            if (parentFolderId.HasValue)
            {
                if (link == null)
                    _context.FOLDER_DOCUMENTS.Add(new FolderDocuments { document_id = documentId, folder_id = parentFolderId.Value, added_by_user_id = actorUserId, added_at = DateTime.UtcNow });
                else
                    link.folder_id = parentFolderId.Value;
            }
            else if (link != null)
            {
                _context.FOLDER_DOCUMENTS.Remove(link);
            }
        }

        private async Task<int?> GetDocumentParentFolderId(int documentId)
        {
            return await _context.FOLDER_DOCUMENTS
                .AsNoTracking()
                .Where(fd => fd.document_id == documentId)
                .Select(fd => (int?)fd.folder_id)
                .FirstOrDefaultAsync();
        }

        private async Task<string> UniqueDocumentName(Guid ownerUserId, int? parentFolderId, string name, int documentId)
        {
            var candidate = name;
            var attempt = 1;
            while (await DocumentNameExistsInParent(ownerUserId, parentFolderId, candidate, documentId))
            {
                candidate = attempt == 1 ? $"{name} (restored)" : $"{name} ({attempt})";
                attempt++;
            }
            return candidate;
        }

        private async Task<string> UniqueFolderName(Guid ownerUserId, int? parentFolderId, string name, int folderId)
        {
            var candidate = name;
            var attempt = 1;
            while (await _context.FOLDERS.AnyAsync(f =>
                f.deleted_at == null &&
                f.owner_user_id == ownerUserId &&
                f.parent_folder_id == parentFolderId &&
                f.folder_id != folderId &&
                f.name == candidate))
            {
                candidate = attempt == 1 ? $"{name} (restored)" : $"{name} ({attempt})";
                attempt++;
            }
            return candidate;
        }

        private static void ClearDocumentTrash(Documents document)
        {
            document.deleted_at = null;
            document.deleted_by = null;
            document.deleted_root_type = null;
            document.deleted_root_id = null;
            document.original_parent_folder_id = null;
        }

        private static void ClearFolderTrash(Folders folder)
        {
            folder.deleted_at = null;
            folder.deleted_by = null;
            folder.deleted_root_type = null;
            folder.deleted_root_id = null;
            folder.original_parent_folder_id = null;
            folder.updated_at = DateTime.UtcNow;
        }

        private static bool CanDeleteDocument(Documents document, DecodedTokenResponse decodedToken)
        {
            return document.user_id == decodedToken.userID || decodedToken.roleID == "admin";
        }

        private static bool IsValidBatchRequest(MoveLibraryItemsRequest request)
        {
            return request.items is { Count: > 0 } && request.items.All(i => i.id > 0 && NormalizeType(i.type) is "document" or "folder");
        }

        private static string? NormalizeType(string? type)
        {
            return string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant();
        }

        private static int FolderDepth(Folders folder, List<Folders> folders)
        {
            var depth = 0;
            var current = folder;
            while (current.parent_folder_id.HasValue)
            {
                var parent = folders.FirstOrDefault(f => f.folder_id == current.parent_folder_id.Value);
                if (parent == null)
                    break;
                depth++;
                current = parent;
            }
            return depth;
        }

        private async Task MoveDocument(int documentId, int? targetFolderId, DecodedTokenResponse decodedToken, List<object> moved, List<object> failed)
        {
            var document = await _context.DOCUMENTS.AsNoTracking().FirstOrDefaultAsync(d => d.document_id == documentId && d.deleted_at == null);
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
            var folder = await _context.FOLDERS.FirstOrDefaultAsync(f => f.folder_id == folderId && f.deleted_at == null);
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
            if (await _context.FOLDERS.AnyAsync(f => f.deleted_at == null && f.owner_user_id == folder.owner_user_id && f.parent_folder_id == targetFolderId && f.folder_id != folderId && f.name == folder.name))
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
                    fd.Document.deleted_at == null &&
                    fd.Folder != null &&
                    fd.Folder.deleted_at == null &&
                    fd.Document.Title == name);
            }

            return await _context.DOCUMENTS.AnyAsync(d =>
                d.user_id == ownerUserId &&
                d.document_id != exceptDocumentId &&
                d.deleted_at == null &&
                d.Title == name &&
                !_context.FOLDER_DOCUMENTS.Any(fd => fd.document_id == d.document_id && fd.Folder != null && fd.Folder.deleted_at == null));
        }

        private async Task<bool> IsDescendant(int sourceFolderId, int targetFolderId)
        {
            var current = await _context.FOLDERS.AsNoTracking().FirstOrDefaultAsync(f => f.folder_id == targetFolderId && f.deleted_at == null);
            while (current?.parent_folder_id != null)
            {
                if (current.parent_folder_id.Value == sourceFolderId)
                    return true;
                current = await _context.FOLDERS.AsNoTracking().FirstOrDefaultAsync(f => f.folder_id == current.parent_folder_id.Value && f.deleted_at == null);
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
