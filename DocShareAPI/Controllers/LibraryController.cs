using DocShareAPI.Data;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers
{
    [Route("api/library")]
    [ApiController]
    public class LibraryController : ControllerBase
    {
        private const long DefaultStorageLimitBytes = 10L * 1024 * 1024 * 1024;

        private readonly DocShareDbContext _context;
        private readonly IFolderPermissionService _permissionService;

        public LibraryController(DocShareDbContext context, IFolderPermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        [HttpGet("my")]
        public async Task<IActionResult> GetMyLibrary(
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] string? search = null,
            [FromQuery] string? sort = "updated_desc",
            [FromQuery] string? fileType = null,
            [FromQuery] Guid? ownerId = null,
            [FromQuery] bool? shared = null,
            [FromQuery] bool? favorite = null)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            return await BuildLibraryResponse(decodedToken.userID, null, paginationParams, search, sort, fileType, ownerId, shared, favorite);
        }

        [HttpGet("/api/folders/{folderId:int}/items")]
        public async Task<IActionResult> GetFolderItems(
            int folderId,
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] string? search = null,
            [FromQuery] string? sort = "updated_desc",
            [FromQuery] string? fileType = null,
            [FromQuery] Guid? ownerId = null,
            [FromQuery] bool? shared = null,
            [FromQuery] bool? favorite = null)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            return await BuildLibraryResponse(decodedToken.userID, folderId, paginationParams, search, sort, fileType, ownerId, shared, favorite);
        }

        [HttpGet("trash")]
        public async Task<IActionResult> GetTrash([FromQuery] PaginationParams paginationParams, [FromQuery] string? sort = "deleted_desc")
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var trashedFolders = await _context.FOLDERS
                .AsNoTracking()
                .Include(f => f.OwnerUser)
                .Include(f => f.ChildFolders)
                .Include(f => f.FolderDocuments)
                    .ThenInclude(fd => fd.Document)
                .Where(f =>
                    f.deleted_at != null &&
                    f.deleted_root_type == "folder" &&
                    f.deleted_root_id == f.folder_id &&
                    f.owner_user_id == decodedToken.userID)
                .ToListAsync();

            var folderTrash = trashedFolders
                .Select(f => ToTrashFolderItem(
                    f,
                    _context.DOCUMENTS.AsNoTracking().Count(d => d.deleted_root_type == "folder" && d.deleted_root_id == f.folder_id),
                    _context.FOLDERS.AsNoTracking().Count(child => child.deleted_root_type == "folder" && child.deleted_root_id == f.folder_id && child.folder_id != f.folder_id),
                    _context.DOCUMENTS.AsNoTracking().Where(d => d.deleted_root_type == "folder" && d.deleted_root_id == f.folder_id).Sum(d => (long)d.file_size)))
                .Cast<object>()
                .ToList();

            var trashedDocuments = await _context.DOCUMENTS
                .AsNoTracking()
                .Include(d => d.Users)
                .Where(d =>
                    d.deleted_at != null &&
                    d.deleted_root_type == "document" &&
                    d.deleted_root_id == d.document_id &&
                    (d.user_id == decodedToken.userID || decodedToken.roleID == "admin"))
                .ToListAsync();

            var documentTrash = trashedDocuments
                .Select(d => ToTrashDocumentItem(d, d.original_parent_folder_id))
                .Cast<object>()
                .ToList();

            var items = SortTrashItems(folderTrash.Concat(documentTrash), sort).ToList();
            var pagedItems = PageItems(items, paginationParams);

            return Ok(new
            {
                folder = TrashFolderContext(),
                items = pagedItems.items,
                pagination = Pagination(pagedItems.currentPage, pagedItems.pageSize, items.Count),
                counts = new { trash = items.Count }
            });
        }

        [HttpGet("favorites")]
        public async Task<IActionResult> GetFavorites([FromQuery] PaginationParams paginationParams)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            return await BuildLibraryResponse(decodedToken.userID, null, paginationParams, null, "updated_desc", null, null, null, true, includeNestedDocuments: true, includeNestedFolders: true);
        }

        [HttpGet("team")]
        public IActionResult GetTeamLibrary([FromQuery] PaginationParams paginationParams)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            return Ok(new
            {
                items = Array.Empty<object>(),
                pagination = Pagination(1, paginationParams.PageSize, 0),
                message = "Team library is not enabled"
            });
        }

        [HttpGet("recent")]
        public async Task<IActionResult> GetRecent([FromQuery] PaginationParams paginationParams)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            return await BuildLibraryResponse(decodedToken.userID, null, paginationParams, null, "updated_desc", null, null, null, null, includeNestedDocuments: true, includeNestedFolders: true);
        }

        [HttpGet("shared-with-me")]
        public async Task<IActionResult> GetSharedWithMe([FromQuery] PaginationParams paginationParams, [FromQuery] string? search = null)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            var memberFolders = await _context.FOLDER_MEMBERS
                .AsNoTracking()
                .Where(m => m.user_id == decodedToken.userID && m.Folder != null && m.Folder.deleted_at == null && m.Folder.owner_user_id != decodedToken.userID)
                .Select(m => new { Folder = m.Folder!, m.role })
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(search))
                memberFolders = memberFolders.Where(m => m.Folder.name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

            var items = memberFolders
                .Select(m => ToFolderItem(m.Folder, NormalizeWorkspaceRole(m.role), isShared: true))
                .Cast<object>()
                .ToList();

            var pagedItems = PageItems(items, paginationParams);
            return Ok(new
            {
                items = pagedItems.items,
                pagination = Pagination(pagedItems.currentPage, pagedItems.pageSize, items.Count)
            });
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchLibrary(
            [FromQuery] string? q,
            [FromQuery] string? scope = "all",
            [FromQuery] int? folderId = null,
            [FromQuery] string? type = null,
            [FromQuery] string? fileType = null,
            [FromQuery] PaginationParams? paginationParams = null)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized(Error("UNAUTHORIZED", "Chưa đăng nhập hoặc token không hợp lệ."));

            paginationParams ??= new PaginationParams();
            var parentFolderId = string.Equals(scope, "current", StringComparison.OrdinalIgnoreCase) ? folderId : null;
            var searchAll = !string.Equals(scope, "current", StringComparison.OrdinalIgnoreCase);
            var response = await BuildLibraryItems(
                decodedToken.userID,
                parentFolderId,
                q,
                "updated_desc",
                fileType,
                null,
                null,
                null,
                includeNestedDocuments: searchAll,
                includeNestedFolders: searchAll);

            if (!string.IsNullOrWhiteSpace(type))
            {
                var normalizedType = type.Trim().ToLowerInvariant();
                response.items = response.items.Where(item => GetItemType(item) == normalizedType).ToList();
            }

            var pagedItems = PageItems(response.items, paginationParams);
            return Ok(new
            {
                items = pagedItems.items,
                pagination = Pagination(pagedItems.currentPage, pagedItems.pageSize, response.items.Count)
            });
        }

        internal async Task<IActionResult> BuildLibraryResponse(
            Guid userId,
            int? parentFolderId,
            PaginationParams paginationParams,
            string? search,
            string? sort,
            string? fileType,
            Guid? ownerId,
            bool? shared,
            bool? favorite,
            bool includeNestedDocuments = false,
            bool includeNestedFolders = false)
        {
            if (parentFolderId.HasValue && !await _permissionService.CanViewFolderAsync(userId, parentFolderId.Value))
                return NotFound(Error("FOLDER_NOT_FOUND", "Không tìm thấy thư mục."));

            var folderInfo = parentFolderId.HasValue
                ? await _context.FOLDERS.AsNoTracking().Include(f => f.OwnerUser).FirstOrDefaultAsync(f => f.folder_id == parentFolderId.Value)
                : null;

            var role = parentFolderId.HasValue
                ? NormalizeWorkspaceRole(await _permissionService.GetRoleAsync(userId, parentFolderId.Value))
                : "owner";

            var libraryItems = await BuildLibraryItems(userId, parentFolderId, search, sort, fileType, ownerId, shared, favorite, includeNestedDocuments, includeNestedFolders);
            var pagedItems = PageItems(libraryItems.items, paginationParams);

            var usedBytes = await _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.user_id == userId && d.deleted_at == null)
                .SumAsync(d => (long)d.file_size);
            var limitBytes = await _context.USERS
                .AsNoTracking()
                .Where(u => u.user_id == userId)
                .Select(u => u.storage_limit_bytes)
                .FirstOrDefaultAsync() ?? DefaultStorageLimitBytes;

            return Ok(new
            {
                folder = parentFolderId.HasValue && folderInfo != null
                    ? ToFolderContext(folderInfo, role, await BuildBreadcrumb(folderInfo))
                    : RootFolderContext(),
                items = pagedItems.items,
                pagination = Pagination(pagedItems.currentPage, pagedItems.pageSize, libraryItems.items.Count),
                counts = new
                {
                    folders = libraryItems.folderCount,
                    documents = libraryItems.documentCount,
                    shared = libraryItems.sharedCount,
                    favorites = await _context.FAVORITES.AsNoTracking().CountAsync(f => f.user_id == userId),
                    trash = 0
                },
                storage = new
                {
                    usedBytes,
                    limitBytes
                }
            });
        }

        internal async Task<(List<object> items, int folderCount, int documentCount, int sharedCount)> BuildLibraryItems(
            Guid userId,
            int? parentFolderId,
            string? search,
            string? sort,
            string? fileType,
            Guid? ownerId,
            bool? shared,
            bool? favorite,
            bool includeNestedDocuments = false,
            bool includeNestedFolders = false)
        {
            var normalizedSearch = search?.Trim();
            var foldersQuery = _context.FOLDERS
                .AsNoTracking()
                .Include(f => f.OwnerUser)
                .Include(f => f.ChildFolders)
                .Include(f => f.FolderDocuments)
                    .ThenInclude(fd => fd.Document)
                .Include(f => f.FolderMembers)
                .Where(f => f.deleted_at == null && (f.owner_user_id == userId || f.FolderMembers.Any(m => m.user_id == userId)));

            if (!includeNestedFolders)
                foldersQuery = foldersQuery.Where(f => f.parent_folder_id == parentFolderId);

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
                foldersQuery = foldersQuery.Where(f => f.name.Contains(normalizedSearch));
            if (ownerId.HasValue)
                foldersQuery = foldersQuery.Where(f => f.owner_user_id == ownerId.Value);
            if (shared.HasValue)
                foldersQuery = shared.Value
                    ? foldersQuery.Where(f => f.visibility != "private" || f.FolderMembers.Any())
                    : foldersQuery.Where(f => f.visibility == "private" && !f.FolderMembers.Any());
            if (favorite == true)
                foldersQuery = foldersQuery.Where(f => _context.FAVORITES.Any(fav => fav.user_id == userId && fav.item_type == "folder" && fav.item_id == f.folder_id));

            var folders = await foldersQuery.ToListAsync();
            var favoriteFolders = await _context.FAVORITES
                .AsNoTracking()
                .Where(f => f.user_id == userId && f.item_type == "folder")
                .Select(f => f.item_id)
                .ToListAsync();

            var folderItems = folders
                .Select(f =>
                {
                    var role = f.owner_user_id == userId ? "owner" : NormalizeWorkspaceRole(f.FolderMembers.FirstOrDefault(m => m.user_id == userId)?.role);
                    return ToFolderItem(f, role, f.visibility != "private" || f.FolderMembers.Count > 0, favoriteFolders.Contains(f.folder_id));
                })
                .Cast<object>()
                .ToList();

            var documentsQuery = _context.DOCUMENTS
                .AsNoTracking()
                .Include(d => d.Users)
                .Where(d => d.deleted_at == null)
                .AsQueryable();

            if (includeNestedDocuments)
            {
                if (!parentFolderId.HasValue)
                    documentsQuery = documentsQuery.Where(d => d.user_id == userId);
            }
            else if (parentFolderId.HasValue)
            {
                documentsQuery = documentsQuery.Where(d => _context.FOLDER_DOCUMENTS.Any(fd => fd.document_id == d.document_id && fd.folder_id == parentFolderId.Value && fd.Folder != null && fd.Folder.deleted_at == null));
            }
            else
            {
                documentsQuery = documentsQuery.Where(d => d.user_id == userId && !_context.FOLDER_DOCUMENTS.Any(fd => fd.document_id == d.document_id && fd.Folder != null && fd.Folder.deleted_at == null));
            }

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
                documentsQuery = documentsQuery.Where(d => d.Title.Contains(normalizedSearch));
            if (!string.IsNullOrWhiteSpace(fileType))
            {
                var normalizedFileType = fileType.Trim().TrimStart('.').ToLowerInvariant();
                documentsQuery = documentsQuery.Where(d => (d.file_type ?? "").ToLower() == normalizedFileType);
            }
            if (ownerId.HasValue)
                documentsQuery = documentsQuery.Where(d => d.user_id == ownerId.Value);
            if (shared.HasValue)
                documentsQuery = documentsQuery.Where(d => d.is_public == shared.Value);
            if (favorite == true)
                documentsQuery = documentsQuery.Where(d => _context.FAVORITES.Any(fav => fav.user_id == userId && fav.item_type == "document" && fav.item_id == d.document_id));

            var documents = await documentsQuery.ToListAsync();
            var favoriteDocuments = await _context.FAVORITES
                .AsNoTracking()
                .Where(f => f.user_id == userId && f.item_type == "document")
                .Select(f => f.item_id)
                .ToListAsync();
            var folderDocumentRole = parentFolderId.HasValue
                ? NormalizeWorkspaceRole(await _permissionService.GetRoleAsync(userId, parentFolderId.Value))
                : null;
            var folderLinks = await _context.FOLDER_DOCUMENTS
                .AsNoTracking()
                .Where(fd => documents.Select(d => d.document_id).Contains(fd.document_id))
                .ToDictionaryAsync(fd => fd.document_id, fd => (int?)fd.folder_id);

            var documentItems = documents
                .Select(d => ToDocumentItem(
                    d,
                    folderLinks.TryGetValue(d.document_id, out var folderId) ? folderId : null,
                    folderDocumentRole ?? (d.user_id == userId ? "owner" : "viewer"),
                    favoriteDocuments.Contains(d.document_id)))
                .Cast<object>()
                .ToList();

            var items = folderItems.Concat(documentItems).ToList();
            items = SortItems(items, sort).ToList();

            return (items, folderItems.Count, documentItems.Count, folderItems.Count(i => IsShared(i)) + documentItems.Count(i => IsShared(i)));
        }

        internal static object ToFolderItem(Folders folder, string? role, bool isShared = false, bool isFavorite = false)
        {
            var permission = NormalizeWorkspaceRole(role);
            var documentCount = folder.FolderDocuments?.Count ?? 0;
            var folderCount = folder.ChildFolders?.Count ?? 0;
            return new
            {
                id = folder.folder_id,
                type = "folder",
                name = folder.name,
                parentFolderId = folder.parent_folder_id,
                ownerId = folder.owner_user_id,
                ownerName = folder.OwnerUser?.full_name ?? folder.OwnerUser?.Username,
                folder.description,
                color = (string?)null,
                childrenCount = documentCount + folderCount,
                documentCount,
                folderCount,
                totalSize = folder.FolderDocuments?.Where(fd => fd.Document != null).Sum(fd => (long)fd.Document!.file_size) ?? 0,
                isFavorite,
                isShared,
                permission,
                permissions = ItemPermissions(permission),
                createdAt = folder.created_at,
                updatedAt = folder.updated_at
            };
        }

        internal static object ToDocumentItem(Documents document, int? parentFolderId, string? role, bool isFavorite = false)
        {
            var permission = NormalizeWorkspaceRole(role);
            var extension = GetExtension(document);
            return new
            {
                id = document.document_id,
                type = "document",
                name = document.Title,
                title = document.Title,
                description = document.Description,
                parentFolderId,
                ownerId = document.user_id,
                ownerName = document.Users?.full_name ?? document.Users?.Username,
                mimeType = ToMimeType(document.file_type, extension),
                extension,
                size = document.file_size,
                thumbnailUrl = document.thumbnail_url,
                previewUrl = $"/api/documents/{document.document_id}/preview",
                downloadUrl = $"/api/documents/{document.document_id}/download",
                status = "ready",
                isFavorite,
                isShared = document.is_public,
                allowDownload = true,
                permission,
                permissions = ItemPermissions(permission),
                createdAt = document.uploaded_at,
                updatedAt = document.uploaded_at
            };
        }

        internal static object ToTrashFolderItem(Folders folder, int? documentCountOverride = null, int? folderCountOverride = null, long? totalSizeOverride = null)
        {
            var documentCount = documentCountOverride ?? folder.FolderDocuments?.Count(fd => fd.Document?.deleted_at != null) ?? 0;
            var folderCount = folderCountOverride ?? folder.ChildFolders?.Count(f => f.deleted_at != null) ?? 0;
            return new
            {
                id = folder.folder_id,
                type = "folder",
                name = folder.name,
                parentFolderId = folder.original_parent_folder_id,
                ownerId = folder.owner_user_id,
                ownerName = folder.OwnerUser?.full_name ?? folder.OwnerUser?.Username,
                documentCount,
                folderCount,
                childrenCount = documentCount + folderCount,
                totalSize = totalSizeOverride ?? folder.FolderDocuments?.Where(fd => fd.Document?.deleted_at != null).Sum(fd => (long)fd.Document!.file_size) ?? 0,
                trashedAt = folder.deleted_at,
                permissions = TrashItemPermissions()
            };
        }

        internal static object ToTrashDocumentItem(Documents document, int? parentFolderId)
        {
            var extension = GetExtension(document);
            return new
            {
                id = document.document_id,
                type = "document",
                name = document.Title,
                title = document.Title,
                parentFolderId,
                ownerId = document.user_id,
                ownerName = document.Users?.full_name ?? document.Users?.Username,
                mimeType = ToMimeType(document.file_type, extension),
                extension,
                size = document.file_size,
                trashedAt = document.deleted_at,
                permissions = TrashItemPermissions()
            };
        }

        private static object TrashFolderContext()
        {
            return new
            {
                id = (int?)null,
                name = "Thùng rác",
                parentFolderId = (int?)null,
                breadcrumb = new[]
                {
                    new { id = (int?)null, name = "Thư viện", href = "/library" },
                    new { id = (int?)null, name = "Thùng rác", href = "/library?area=trash" }
                },
                permissions = new { canView = true, canDelete = true }
            };
        }

        private static object TrashItemPermissions()
        {
            return new
            {
                canView = true,
                canDownload = false,
                canUpload = false,
                canCreateFolder = false,
                canRename = false,
                canMove = false,
                canCopy = false,
                canShare = false,
                canDelete = true,
                canManageMembers = false
            };
        }

        internal static object ItemPermissions(string? permission)
        {
            var role = NormalizeWorkspaceRole(permission);
            var canEdit = role is "owner" or "editor";
            return new
            {
                canView = role != null,
                canDownload = role != null,
                canUpload = canEdit,
                canCreateFolder = canEdit,
                canRename = canEdit,
                canMove = canEdit,
                canCopy = canEdit,
                canShare = canEdit,
                canDelete = canEdit,
                canManageMembers = role == "owner"
            };
        }

        internal static string NormalizeWorkspaceRole(string? role)
        {
            return role switch
            {
                "owner" => "owner",
                "admin" => "owner",
                "editor" => "editor",
                "contributor" => "editor",
                "commenter" => "viewer",
                "public" => "viewer",
                "viewer" => "viewer",
                _ => "viewer"
            };
        }

        internal static object Pagination(int currentPage, int pageSize, int totalCount)
        {
            return new
            {
                currentPage,
                pageSize,
                totalCount,
                totalPages = (int)Math.Ceiling(totalCount / (double)Math.Max(pageSize, 1))
            };
        }

        internal static object Error(string code, string message, object? details = null)
        {
            return new { success = false, code, message, details };
        }

        private static object RootFolderContext()
        {
            return new
            {
                id = (int?)null,
                name = "Tài liệu của tôi",
                parentFolderId = (int?)null,
                breadcrumb = new[] { new { id = (int?)null, name = "Tài liệu của tôi", href = "/documents/my" } },
                permission = "owner",
                permissions = ItemPermissions("owner")
            };
        }

        private static object ToFolderContext(Folders folder, string role, List<object> breadcrumb)
        {
            return new
            {
                id = folder.folder_id,
                name = folder.name,
                parentFolderId = folder.parent_folder_id,
                folder.description,
                isShared = folder.visibility != "private" || folder.FolderMembers.Count > 0,
                permission = role,
                breadcrumb,
                permissions = ItemPermissions(role)
            };
        }

        private async Task<List<object>> BuildBreadcrumb(Folders folder)
        {
            var folders = await _context.FOLDERS.AsNoTracking().ToListAsync();
            var breadcrumb = new List<object>
            {
                new { id = (int?)null, name = "Tài liệu của tôi", href = "/documents/my" }
            };

            var stack = new Stack<Folders>();
            var current = folder;
            while (current != null)
            {
                stack.Push(current);
                current = current.parent_folder_id.HasValue
                    ? folders.FirstOrDefault(f => f.folder_id == current.parent_folder_id.Value)
                    : null;
            }

            while (stack.Count > 0)
            {
                var item = stack.Pop();
                breadcrumb.Add(new { id = (int?)item.folder_id, name = item.name, href = $"/documents/folders/{item.folder_id}" });
            }

            return breadcrumb;
        }

        private static (List<object> items, int currentPage, int pageSize) PageItems(List<object> items, PaginationParams paginationParams)
        {
            var currentPage = paginationParams.PageNumber <= 0 ? 1 : paginationParams.PageNumber;
            var pageSize = paginationParams.PageSize <= 0 ? 50 : paginationParams.PageSize;
            return (items.Skip((currentPage - 1) * pageSize).Take(pageSize).ToList(), currentPage, pageSize);
        }

        private static IEnumerable<object> SortItems(IEnumerable<object> items, string? sort)
        {
            return (sort ?? "updated_desc").ToLowerInvariant() switch
            {
                "name_asc" => items.OrderBy(GetItemName),
                "name_desc" => items.OrderByDescending(GetItemName),
                "updated_asc" => items.OrderBy(GetItemUpdatedAt),
                "type" => items.OrderBy(GetItemType).ThenBy(GetItemName),
                "size_desc" => items.OrderByDescending(GetItemSize),
                _ => items.OrderByDescending(GetItemUpdatedAt)
            };
        }

        private static IEnumerable<object> SortTrashItems(IEnumerable<object> items, string? sort)
        {
            return (sort ?? "deleted_desc").ToLowerInvariant() switch
            {
                "deleted_asc" => items.OrderBy(GetItemTrashedAt),
                "name_asc" => items.OrderBy(GetItemName),
                "name_desc" => items.OrderByDescending(GetItemName),
                _ => items.OrderByDescending(GetItemTrashedAt)
            };
        }

        private static string GetItemType(object item) => item.GetType().GetProperty("type")?.GetValue(item)?.ToString() ?? "";
        private static string GetItemName(object item) => item.GetType().GetProperty("name")?.GetValue(item)?.ToString() ?? "";
        private static long GetItemSize(object item)
        {
            var value = item.GetType().GetProperty("size")?.GetValue(item)
                ?? item.GetType().GetProperty("totalSize")?.GetValue(item);

            return value switch
            {
                long longValue => longValue,
                int intValue => intValue,
                _ => 0
            };
        }
        private static DateTime GetItemUpdatedAt(object item) => item.GetType().GetProperty("updatedAt")?.GetValue(item) as DateTime? ?? DateTime.MinValue;
        private static DateTime GetItemTrashedAt(object item) => item.GetType().GetProperty("trashedAt")?.GetValue(item) as DateTime? ?? DateTime.MinValue;
        private static bool IsShared(object item) => item.GetType().GetProperty("isShared")?.GetValue(item) as bool? ?? false;

        private static string GetExtension(Documents document)
        {
            var titleExtension = Path.GetExtension(document.Title)?.TrimStart('.').ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(titleExtension))
                return titleExtension;
            return document.file_type?.TrimStart('.').ToLowerInvariant() ?? "";
        }

        private static string ToMimeType(string? fileType, string extension)
        {
            var normalized = (fileType ?? extension).ToLowerInvariant();
            return normalized switch
            {
                "pdf" => "application/pdf",
                "doc" => "application/msword",
                "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "txt" => "text/plain",
                _ when normalized.Contains("/") => normalized,
                _ => "application/octet-stream"
            };
        }
    }
}
