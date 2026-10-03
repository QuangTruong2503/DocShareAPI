using DocShareAPI.Data;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers
{
    [Route("api/favorites")]
    [ApiController]
    public class FavoritesController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly IFolderPermissionService _folderPermissionService;
        private readonly IAuditLogService _auditLogService;

        public FavoritesController(
            DocShareDbContext context,
            IFolderPermissionService folderPermissionService,
            IAuditLogService auditLogService)
        {
            _context = context;
            _folderPermissionService = folderPermissionService;
            _auditLogService = auditLogService;
        }

        [HttpGet]
        public async Task<IActionResult> GetFavorites([FromQuery] PaginationParams paginationParams, [FromQuery] string? type = null)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var query = _context.FAVORITES
                .AsNoTracking()
                .Where(f => f.user_id == decodedToken.userID);

            if (!string.IsNullOrWhiteSpace(type))
                query = query.Where(f => f.item_type == type.Trim().ToLowerInvariant());

            var favorites = await query
                .OrderByDescending(f => f.created_at)
                .ToListAsync();

            var items = new List<object>();
            foreach (var favorite in favorites)
            {
                if (!await CanViewItem(decodedToken, favorite.item_id, favorite.item_type)) continue;
                var item = await BuildFavoriteItem(favorite, decodedToken);
                if (item != null)
                    items.Add(item);
            }

            var page = paginationParams.PageNumber <= 0 ? 1 : paginationParams.PageNumber;
            var pageSize = paginationParams.PageSize <= 0 ? 50 : paginationParams.PageSize;
            var pagedItems = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Ok(new
            {
                success = true,
                items = pagedItems,
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount = items.Count,
                    totalPages = (int)Math.Ceiling(items.Count / (double)Math.Max(pageSize, 1))
                }
            });
        }

        [HttpPut("/api/library-items/{itemId:int}/favorite")]
        public async Task<IActionResult> SetFavorite(int itemId, [FromBody] FavoriteRequest request)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var itemType = NormalizeType(request.type);
            if (itemId <= 0 || itemType == null)
                return BadRequest(Error("VALIDATION_ERROR", "itemId và type hợp lệ là bắt buộc."));

            if (!await CanViewItem(decodedToken, itemId, itemType))
                return NotFound(Error("ITEM_NOT_FOUND", "Không tìm thấy item hoặc bạn không có quyền xem."));

            var favorite = await _context.FAVORITES
                .FirstOrDefaultAsync(f => f.user_id == decodedToken.userID && f.item_id == itemId && f.item_type == itemType);

            if (request.favorite)
            {
                if (favorite == null)
                {
                    favorite = new Favorites
                    {
                        user_id = decodedToken.userID,
                        item_id = itemId,
                        item_type = itemType,
                        created_at = DateTime.UtcNow
                    };
                    _context.FAVORITES.Add(favorite);
                    await _context.SaveChangesAsync();
                    await _auditLogService.LogAsync(decodedToken.userID, "favorite.created", itemType, itemId.ToString(), null, HttpContext.Connection.RemoteIpAddress?.ToString());
                }
            }
            else if (favorite != null)
            {
                _context.FAVORITES.Remove(favorite);
                await _context.SaveChangesAsync();
                await _auditLogService.LogAsync(decodedToken.userID, "favorite.deleted", itemType, itemId.ToString(), null, HttpContext.Connection.RemoteIpAddress?.ToString());
            }

            return Ok(new { success = true, id = itemId, type = itemType, isFavorite = request.favorite });
        }

        private async Task<object?> BuildFavoriteItem(Favorites favorite, DecodedTokenResponse token)
        {
            if (favorite.item_type == "document")
            {
                var document = await _context.DOCUMENTS
                    .AsNoTracking()
                    .Include(d => d.Users)
                    .FirstOrDefaultAsync(d => d.document_id == favorite.item_id && d.deleted_at == null);

                if (document == null)
                    return null;

                var folderId = await _context.FOLDER_DOCUMENTS
                    .AsNoTracking()
                    .Where(fd => fd.document_id == document.document_id)
                    .Select(fd => (int?)fd.folder_id)
                    .FirstOrDefaultAsync();

                return LibraryController.ToDocumentItem(document, folderId, document.user_id == token.userID ? "owner" : folderId.HasValue ? await _folderPermissionService.GetRoleAsync(token.userID, folderId.Value) ?? "viewer" : "viewer", true);
            }

            if (favorite.item_type == "folder")
            {
                var folder = await _context.FOLDERS
                    .AsNoTracking()
                    .Include(f => f.OwnerUser)
                    .Include(f => f.ChildFolders)
                    .Include(f => f.FolderDocuments)
                    .FirstOrDefaultAsync(f => f.folder_id == favorite.item_id && f.deleted_at == null);

                return folder == null ? null : LibraryController.ToFolderItem(folder, await _folderPermissionService.GetRoleAsync(token.userID, folder.folder_id), folder.visibility != "private", true);
            }

            var collection = await _context.COLLECTIONS
                .AsNoTracking()
                .Include(c => c.Users)
                .FirstOrDefaultAsync(c => c.collection_id == favorite.item_id);

            return collection == null ? null : new
            {
                id = collection.collection_id,
                type = "collection",
                name = collection.Name,
                description = collection.Description,
                ownerId = collection.user_id,
                ownerName = collection.Users?.full_name ?? collection.Users?.Username,
                isShared = collection.is_public,
                isFavorite = true,
                createdAt = collection.created_at
            };
        }

        private async Task<bool> CanViewItem(DecodedTokenResponse decodedToken, int itemId, string itemType)
        {
            if (itemType == "document")
            {
                var document = await _context.DOCUMENTS
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.document_id == itemId && d.deleted_at == null);

                if (document == null)
                    return false;

                if (document.is_public || document.user_id == decodedToken.userID || decodedToken.roleID == "admin")
                    return true;

                var folderId = await _context.FOLDER_DOCUMENTS
                    .AsNoTracking()
                    .Where(fd => fd.document_id == itemId)
                    .Select(fd => (int?)fd.folder_id)
                    .FirstOrDefaultAsync();

                return folderId.HasValue && await _folderPermissionService.CanViewFolderAsync(decodedToken.userID, folderId.Value);
            }

            if (itemType == "folder")
                return await _folderPermissionService.CanViewFolderAsync(decodedToken.userID, itemId);

            return await _context.COLLECTIONS.AnyAsync(c =>
                c.collection_id == itemId &&
                (c.is_public || c.user_id == decodedToken.userID || decodedToken.roleID == "admin"));
        }

        private static string? NormalizeType(string? type)
        {
            var normalized = type?.Trim().ToLowerInvariant();
            return normalized is "document" or "folder" or "collection" ? normalized : null;
        }

        private static object Error(string code, string message, object? details = null) => new { success = false, code, message, details };
    }

    public class FavoriteRequest
    {
        public string? type { get; set; }
        public bool favorite { get; set; }
    }
}
