using DocShareAPI.Data;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers
{
    [Route("api/library")]
    [ApiController]
    public class LibraryController : ControllerBase
    {
        private readonly DocShareDbContext _context;

        public LibraryController(DocShareDbContext context)
        {
            _context = context;
        }

        [HttpGet("my")]
        public async Task<IActionResult> GetMyLibrary(
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] string? search = null)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            var normalizedSearch = search?.Trim();

            var documentsQuery = _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.user_id == decodedToken.userID);

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
                documentsQuery = documentsQuery.Where(d => d.Title.Contains(normalizedSearch));

            var foldersQuery = _context.FOLDERS
                .AsNoTracking()
                .Where(f => f.owner_user_id == decodedToken.userID && f.parent_folder_id == null);

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
                foldersQuery = foldersQuery.Where(f => f.name.Contains(normalizedSearch));

            var sharedFoldersQuery = _context.FOLDER_MEMBERS
                .AsNoTracking()
                .Where(m => m.user_id == decodedToken.userID && m.Folder != null && m.Folder.owner_user_id != decodedToken.userID);

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
                sharedFoldersQuery = sharedFoldersQuery.Where(m => m.Folder!.name.Contains(normalizedSearch));

            var documents = await documentsQuery
                .OrderByDescending(d => d.uploaded_at)
                .Select(d => new
                {
                    d.document_id,
                    d.Title,
                    d.Description,
                    d.thumbnail_url,
                    d.file_type,
                    d.file_size,
                    d.pages,
                    d.download_count,
                    d.uploaded_at,
                    d.is_public,
                    folder_id = _context.FOLDER_DOCUMENTS
                        .Where(fd => fd.document_id == d.document_id)
                        .Select(fd => (int?)fd.folder_id)
                        .FirstOrDefault()
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            var folders = await foldersQuery
                .OrderBy(f => f.name)
                .Select(f => new
                {
                    f.folder_id,
                    f.owner_user_id,
                    f.parent_folder_id,
                    f.name,
                    f.description,
                    f.visibility,
                    f.created_at,
                    f.updated_at,
                    document_count = f.FolderDocuments.Count,
                    member_count = f.FolderMembers.Count,
                    current_user_role = "owner"
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            var sharedFolders = await sharedFoldersQuery
                .OrderBy(m => m.Folder!.name)
                .Select(m => new
                {
                    m.Folder!.folder_id,
                    m.Folder.owner_user_id,
                    owner = m.Folder.OwnerUser == null ? null : new
                    {
                        m.Folder.OwnerUser.user_id,
                        m.Folder.OwnerUser.Username,
                        m.Folder.OwnerUser.full_name,
                        m.Folder.OwnerUser.avatar_url
                    },
                    m.Folder.parent_folder_id,
                    m.Folder.name,
                    m.Folder.description,
                    m.Folder.visibility,
                    current_user_role = m.role
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(new
            {
                success = true,
                documents,
                folders,
                shared_folders = sharedFolders,
                counts = new
                {
                    documents = documents.TotalCount,
                    folders = folders.TotalCount,
                    shared_folders = sharedFolders.TotalCount
                },
                pagination = new
                {
                    documents = ToPagination(documents),
                    folders = ToPagination(folders),
                    shared_folders = ToPagination(sharedFolders)
                }
            });
        }

        private static object ToPagination<T>(PagedList<T> data)
        {
            return new
            {
                data.CurrentPage,
                data.PageSize,
                data.TotalCount,
                data.TotalPages
            };
        }
    }
}
