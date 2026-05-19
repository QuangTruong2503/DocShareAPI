using DocShareAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Services
{
    public interface IFolderPermissionService
    {
        Task<string?> GetRoleAsync(Guid? userId, int folderId);
        Task<bool> CanViewFolderAsync(Guid? userId, int folderId);
        Task<bool> CanCommentInFolderAsync(Guid userId, int folderId);
        Task<bool> CanAddDocumentToFolderAsync(Guid userId, int folderId);
        Task<bool> CanRemoveDocumentFromFolderAsync(Guid userId, int folderId);
        Task<bool> CanEditFolderAsync(Guid userId, int folderId);
        Task<bool> CanManageFolderMembersAsync(Guid userId, int folderId);
        Task<bool> CanDeleteFolderAsync(Guid userId, int folderId);
    }

    public class FolderPermissionService : IFolderPermissionService
    {
        private readonly DocShareDbContext _context;

        public FolderPermissionService(DocShareDbContext context)
        {
            _context = context;
        }

        public async Task<string?> GetRoleAsync(Guid? userId, int folderId)
        {
            var folder = await _context.FOLDERS
                .AsNoTracking()
                .Where(f => f.folder_id == folderId)
                .Select(f => new { f.owner_user_id, f.visibility })
                .FirstOrDefaultAsync();

            if (folder == null)
                return null;

            if (userId.HasValue && folder.owner_user_id == userId.Value)
                return "owner";

            if (userId.HasValue)
            {
                var memberRole = await _context.FOLDER_MEMBERS
                    .AsNoTracking()
                    .Where(m => m.folder_id == folderId && m.user_id == userId.Value)
                    .Select(m => m.role)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrWhiteSpace(memberRole))
                    return memberRole;
            }

            return folder.visibility == "public" ? "public" : null;
        }

        public async Task<bool> CanViewFolderAsync(Guid? userId, int folderId)
        {
            return await GetRoleAsync(userId, folderId) != null;
        }

        public async Task<bool> CanCommentInFolderAsync(Guid userId, int folderId)
        {
            var role = await GetRoleAsync(userId, folderId);
            return role is "owner" or "admin" or "editor" or "contributor" or "commenter";
        }

        public async Task<bool> CanAddDocumentToFolderAsync(Guid userId, int folderId)
        {
            var role = await GetRoleAsync(userId, folderId);
            return role is "owner" or "admin" or "editor" or "contributor";
        }

        public async Task<bool> CanRemoveDocumentFromFolderAsync(Guid userId, int folderId)
        {
            var role = await GetRoleAsync(userId, folderId);
            return role is "owner" or "admin" or "editor";
        }

        public async Task<bool> CanEditFolderAsync(Guid userId, int folderId)
        {
            var role = await GetRoleAsync(userId, folderId);
            return role is "owner" or "admin" or "editor";
        }

        public async Task<bool> CanManageFolderMembersAsync(Guid userId, int folderId)
        {
            var role = await GetRoleAsync(userId, folderId);
            return role is "owner" or "admin";
        }

        public async Task<bool> CanDeleteFolderAsync(Guid userId, int folderId)
        {
            var role = await GetRoleAsync(userId, folderId);
            return role == "owner";
        }
    }
}
