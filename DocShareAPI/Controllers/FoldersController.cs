using DocShareAPI.Data;
using DocShareAPI.DataTransferObject.Folders;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace DocShareAPI.Controllers
{
    [Route("api/folders")]
    [ApiController]
    public class FoldersController : ControllerBase
    {
        private static readonly string[] FolderVisibilities = ["private", "shared", "public"];
        private static readonly string[] FolderRoles = ["viewer", "commenter", "contributor", "editor", "admin"];

        private readonly DocShareDbContext _context;
        private readonly IFolderPermissionService _permissionService;
        private readonly INotificationService _notificationService;

        public FoldersController(
            DocShareDbContext context,
            IFolderPermissionService permissionService,
            INotificationService notificationService)
        {
            _context = context;
            _permissionService = permissionService;
            _notificationService = notificationService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateFolder([FromBody] CreateFolderDto dto)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            var name = dto.name?.Trim();
            var visibility = NormalizeVisibility(dto.visibility);

            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { success = false, code = "VALIDATION_ERROR", message = "Tên thư mục là bắt buộc." });
            if (visibility == null)
                return UnprocessableEntity(new { success = false, code = "VALIDATION_ERROR", message = "visibility không hợp lệ." });

            if (dto.parent_folder_id.HasValue && !await _permissionService.CanAddDocumentToFolderAsync(decodedToken.userID, dto.parent_folder_id.Value))
                return Forbid();

            if (await FolderNameExists(decodedToken.userID, dto.parent_folder_id, name, null))
                return Conflict(new { success = false, code = "FOLDER_NAME_EXISTS", message = "Tên thư mục đã tồn tại trong cùng cấp." });

            var folder = new Folders
            {
                owner_user_id = decodedToken.userID,
                parent_folder_id = dto.parent_folder_id,
                name = name,
                description = dto.description,
                visibility = visibility,
                created_at = DateTime.UtcNow,
                updated_at = DateTime.UtcNow
            };

            _context.FOLDERS.Add(folder);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, folder = ToFolderResponse(folder, "owner") });
        }

        [HttpGet("my")]
        public async Task<IActionResult> GetMyFolders(
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] int? parent_folder_id = null,
            [FromQuery] string? search = null)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            var query = _context.FOLDERS
                .AsNoTracking()
                .Where(f => f.owner_user_id == decodedToken.userID && f.parent_folder_id == parent_folder_id);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(f => f.name.Contains(search.Trim()));

            var folders = await query
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
                    member_count = f.FolderMembers.Count
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(PagedResponse(folders));
        }

        [HttpGet("shared-with-me")]
        public async Task<IActionResult> GetSharedWithMe([FromQuery] PaginationParams paginationParams, [FromQuery] string? search = null)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            var query = _context.FOLDER_MEMBERS
                .AsNoTracking()
                .Where(m => m.user_id == decodedToken.userID && m.Folder != null && m.Folder.owner_user_id != decodedToken.userID);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(m => m.Folder!.name.Contains(search.Trim()));

            var folders = await query
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

            return Ok(PagedResponse(folders));
        }

        [HttpGet("{folderId:int}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetFolderDetail(int folderId)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            var userId = decodedToken?.userID;

            if (!await _permissionService.CanViewFolderAsync(userId, folderId))
                return NotFound(new { success = false, code = "FOLDER_NOT_FOUND", message = "Không tìm thấy thư mục." });

            var folder = await _context.FOLDERS
                .AsNoTracking()
                .Where(f => f.folder_id == folderId)
                .Select(f => new
                {
                    f.folder_id,
                    f.owner_user_id,
                    owner = f.OwnerUser == null ? null : new
                    {
                        f.OwnerUser.user_id,
                        f.OwnerUser.Username,
                        f.OwnerUser.full_name,
                        f.OwnerUser.avatar_url
                    },
                    f.parent_folder_id,
                    f.name,
                    f.description,
                    f.visibility,
                    f.created_at,
                    f.updated_at,
                    document_count = f.FolderDocuments.Count,
                    member_count = f.FolderMembers.Count
                })
                .FirstOrDefaultAsync();

            var role = await _permissionService.GetRoleAsync(userId, folderId);
            return Ok(new
            {
                success = true,
                folder,
                current_user_role = role,
                permissions = PermissionResponse(role)
            });
        }

        [HttpPatch("{folderId:int}")]
        public async Task<IActionResult> UpdateFolder(int folderId, [FromBody] UpdateFolderDto dto)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            if (!await _permissionService.CanEditFolderAsync(decodedToken.userID, folderId))
                return Forbid();

            var folder = await _context.FOLDERS.FirstOrDefaultAsync(f => f.folder_id == folderId);
            if (folder == null)
                return NotFound(new { success = false, code = "FOLDER_NOT_FOUND", message = "Không tìm thấy thư mục." });

            var role = await _permissionService.GetRoleAsync(decodedToken.userID, folderId);

            if (dto.name != null)
            {
                var name = dto.name.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    return BadRequest(new { success = false, code = "VALIDATION_ERROR", message = "Tên thư mục không được rỗng." });
                if (await FolderNameExists(folder.owner_user_id, folder.parent_folder_id, name, folder.folder_id))
                    return Conflict(new { success = false, code = "FOLDER_NAME_EXISTS", message = "Tên thư mục đã tồn tại trong cùng cấp." });
                folder.name = name;
            }

            if (dto.description != null)
                folder.description = dto.description;

            if (dto.visibility != null)
            {
                if (role is not ("owner" or "admin"))
                    return Forbid();
                var visibility = NormalizeVisibility(dto.visibility);
                if (visibility == null)
                    return UnprocessableEntity(new { success = false, code = "VALIDATION_ERROR", message = "visibility không hợp lệ." });
                folder.visibility = visibility;
            }

            folder.updated_at = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, folder = ToFolderResponse(folder, role) });
        }

        [HttpDelete("{folderId:int}")]
        public async Task<IActionResult> DeleteFolder(int folderId)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            if (!await _permissionService.CanDeleteFolderAsync(decodedToken.userID, folderId))
                return Forbid();

            var folder = await _context.FOLDERS.FirstOrDefaultAsync(f => f.folder_id == folderId);
            if (folder == null)
                return NotFound(new { success = false, code = "FOLDER_NOT_FOUND", message = "Không tìm thấy thư mục." });

            _context.FOLDERS.Remove(folder);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, deleted_folder_id = folderId });
        }

        [HttpGet("{folderId:int}/documents")]
        [AllowAnonymous]
        public async Task<IActionResult> GetFolderDocuments(
            int folderId,
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] string? search = null,
            [FromQuery] string? file_type = null)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            var userId = decodedToken?.userID;

            if (!await _permissionService.CanViewFolderAsync(userId, folderId))
                return NotFound(new { success = false, code = "FOLDER_NOT_FOUND", message = "Không tìm thấy thư mục." });

            var query = _context.FOLDER_DOCUMENTS
                .AsNoTracking()
                .Where(fd => fd.folder_id == folderId);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(fd => fd.Document!.Title.Contains(search.Trim()));

            if (!string.IsNullOrWhiteSpace(file_type))
                query = query.Where(fd => fd.Document!.file_type == file_type.Trim());

            var documents = await query
                .OrderByDescending(fd => fd.added_at)
                .Select(fd => new
                {
                    fd.folder_id,
                    fd.document_id,
                    fd.added_by_user_id,
                    fd.added_at,
                    document = new
                    {
                        fd.Document!.document_id,
                        fd.Document.user_id,
                        fd.Document.Title,
                        fd.Document.Description,
                        fd.Document.file_url,
                        fd.Document.thumbnail_url,
                        fd.Document.file_type,
                        fd.Document.file_size,
                        fd.Document.pages,
                        fd.Document.download_count,
                        fd.Document.uploaded_at,
                        fd.Document.is_public,
                        uploader = fd.Document.Users == null ? null : new
                        {
                            fd.Document.Users.user_id,
                            fd.Document.Users.Username,
                            fd.Document.Users.full_name,
                            fd.Document.Users.avatar_url
                        }
                    }
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(PagedResponse(documents));
        }

        [HttpPost("{folderId:int}/documents")]
        public async Task<IActionResult> AddDocumentToFolder(int folderId, [FromBody] FolderDocumentDto dto)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            if (!await _permissionService.CanAddDocumentToFolderAsync(decodedToken.userID, folderId))
                return Forbid();

            var folder = await _context.FOLDERS.AsNoTracking().FirstOrDefaultAsync(f => f.folder_id == folderId);
            if (folder == null)
                return NotFound(new { success = false, code = "FOLDER_NOT_FOUND", message = "Không tìm thấy thư mục." });

            var document = await _context.DOCUMENTS.AsNoTracking().FirstOrDefaultAsync(d => d.document_id == dto.document_id);
            if (document == null)
                return NotFound(new { success = false, code = "DOCUMENT_NOT_FOUND", message = "Không tìm thấy tài liệu." });
            if (!CanAccessDocument(document.user_id, document.is_public, decodedToken))
                return Forbid();

            var existingFolderId = await _context.FOLDER_DOCUMENTS
                .Where(fd => fd.document_id == dto.document_id)
                .Select(fd => (int?)fd.folder_id)
                .FirstOrDefaultAsync();

            if (existingFolderId.HasValue)
                return Conflict(new { success = false, code = "DOCUMENT_ALREADY_IN_FOLDER", message = "Tài liệu đã nằm trong một thư mục khác.", folder_id = existingFolderId.Value });

            FolderDocuments? folderDocument = null;
            var strategy = _context.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();

                folderDocument = new FolderDocuments
                {
                    folder_id = folderId,
                    document_id = dto.document_id,
                    added_by_user_id = decodedToken.userID,
                    added_at = DateTime.UtcNow
                };

                _context.FOLDER_DOCUMENTS.Add(folderDocument);
                await _context.SaveChangesAsync();

                if (folder.owner_user_id != decodedToken.userID)
                {
                    await _notificationService.CreateAsync(
                        recipientUserId: folder.owner_user_id,
                        actorUserId: decodedToken.userID,
                        type: "folder_document_added",
                        title: "Có tài liệu mới trong thư mục",
                        message: $"Tài liệu \"{document.Title}\" đã được thêm vào thư mục \"{folder.name}\".",
                        relatedDocumentId: document.document_id,
                        relatedFolderId: folder.folder_id,
                        targetUrl: $"/library/folders/{folder.folder_id}/documents",
                        metadata: new { folder_id = folder.folder_id, document_id = document.document_id });
                }

                await transaction.CommitAsync();
            });

            return Ok(new { success = true, folder_document = folderDocument });
        }

        [HttpPatch("/api/documents/{documentId:int}/folder")]
        public async Task<IActionResult> MoveDocumentToFolder(int documentId, [FromBody] MoveDocumentFolderDto dto)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            var document = await _context.DOCUMENTS.AsNoTracking().FirstOrDefaultAsync(d => d.document_id == documentId);
            if (document == null)
                return NotFound(new { success = false, code = "DOCUMENT_NOT_FOUND", message = "Không tìm thấy tài liệu." });
            if (!CanAccessDocument(document.user_id, document.is_public, decodedToken))
                return Forbid();

            var current = await _context.FOLDER_DOCUMENTS.FirstOrDefaultAsync(fd => fd.document_id == documentId);
            if (current != null && !await _permissionService.CanRemoveDocumentFromFolderAsync(decodedToken.userID, current.folder_id))
                return Forbid();
            if (!await _permissionService.CanAddDocumentToFolderAsync(decodedToken.userID, dto.target_folder_id))
                return Forbid();

            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();

                if (current == null)
                {
                    _context.FOLDER_DOCUMENTS.Add(new FolderDocuments
                    {
                        folder_id = dto.target_folder_id,
                        document_id = documentId,
                        added_by_user_id = decodedToken.userID,
                        added_at = DateTime.UtcNow
                    });
                }
                else
                {
                    current.folder_id = dto.target_folder_id;
                    current.added_by_user_id = decodedToken.userID;
                    current.added_at = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            });

            return Ok(new { success = true, document_id = documentId, target_folder_id = dto.target_folder_id });
        }

        [HttpDelete("{folderId:int}/documents/{documentId:int}")]
        public async Task<IActionResult> RemoveDocumentFromFolder(int folderId, int documentId)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            if (!await _permissionService.CanRemoveDocumentFromFolderAsync(decodedToken.userID, folderId))
                return Forbid();

            var folderDocument = await _context.FOLDER_DOCUMENTS
                .FirstOrDefaultAsync(fd => fd.folder_id == folderId && fd.document_id == documentId);

            if (folderDocument == null)
                return NotFound(new { success = false, message = "Tài liệu không nằm trong thư mục này." });

            _context.FOLDER_DOCUMENTS.Remove(folderDocument);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, folder_id = folderId, document_id = documentId });
        }

        [HttpGet("{folderId:int}/members")]
        public async Task<IActionResult> GetMembers(int folderId, [FromQuery] PaginationParams paginationParams)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            if (!await _permissionService.CanViewFolderAsync(decodedToken.userID, folderId))
                return Forbid();

            var members = await _context.FOLDER_MEMBERS
                .AsNoTracking()
                .Where(m => m.folder_id == folderId)
                .OrderBy(m => m.User!.Username)
                .Select(m => new
                {
                    m.folder_id,
                    m.user_id,
                    user = m.User == null ? null : new
                    {
                        m.User.user_id,
                        m.User.Username,
                        m.User.full_name,
                        m.User.avatar_url
                    },
                    m.role,
                    m.invited_by_user_id
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(PagedResponse(members));
        }

        [HttpPost("{folderId:int}/members")]
        public async Task<IActionResult> AddMember(int folderId, [FromBody] AddFolderMemberDto dto)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            if (!await _permissionService.CanManageFolderMembersAsync(decodedToken.userID, folderId))
                return Forbid();

            var role = NormalizeRole(dto.role);
            if (role == null)
                return UnprocessableEntity(new { success = false, code = "INVALID_ROLE", message = "Role không hợp lệ." });

            var folder = await _context.FOLDERS.AsNoTracking().FirstOrDefaultAsync(f => f.folder_id == folderId);
            if (folder == null)
                return NotFound(new { success = false, code = "FOLDER_NOT_FOUND", message = "Không tìm thấy thư mục." });
            if (folder.owner_user_id == dto.user_id)
                return Conflict(new { success = false, message = "Owner không cần thêm vào danh sách member." });
            if (!await _context.USERS.AnyAsync(u => u.user_id == dto.user_id))
                return NotFound(new { success = false, message = "Không tìm thấy user." });
            if (await _context.FOLDER_MEMBERS.AnyAsync(m => m.folder_id == folderId && m.user_id == dto.user_id))
                return Conflict(new { success = false, code = "MEMBER_ALREADY_EXISTS", message = "User đã là thành viên." });

            var member = new FolderMembers
            {
                folder_id = folderId,
                user_id = dto.user_id,
                role = role,
                invited_by_user_id = decodedToken.userID
            };

            _context.FOLDER_MEMBERS.Add(member);
            await _context.SaveChangesAsync();

            await _notificationService.CreateAsync(
                recipientUserId: dto.user_id,
                actorUserId: decodedToken.userID,
                type: "folder_member_added",
                title: "Bạn đã được thêm vào thư mục",
                message: $"Bạn đã được thêm vào thư mục \"{folder.name}\" với quyền {role}.",
                relatedFolderId: folderId,
                targetUrl: $"/library/folders/{folderId}",
                metadata: new { folder_id = folderId, role });

            return Ok(new { success = true, member });
        }

        [HttpPatch("{folderId:int}/members/{memberUserId:guid}")]
        public async Task<IActionResult> UpdateMemberRole(int folderId, Guid memberUserId, [FromBody] UpdateFolderMemberRoleDto dto)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            if (!await _permissionService.CanManageFolderMembersAsync(decodedToken.userID, folderId))
                return Forbid();

            if (memberUserId == decodedToken.userID)
                return BadRequest(new { success = false, message = "Không thể tự đổi quyền của chính mình." });

            var role = NormalizeRole(dto.role);
            if (role == null)
                return UnprocessableEntity(new { success = false, code = "INVALID_ROLE", message = "Role không hợp lệ." });

            var member = await _context.FOLDER_MEMBERS.FirstOrDefaultAsync(m => m.folder_id == folderId && m.user_id == memberUserId);
            if (member == null)
                return NotFound(new { success = false, message = "Không tìm thấy member." });

            member.role = role;
            await _context.SaveChangesAsync();

            await _notificationService.CreateAsync(
                recipientUserId: memberUserId,
                actorUserId: decodedToken.userID,
                type: "folder_role_changed",
                title: "Quyền trong thư mục đã thay đổi",
                message: $"Quyền của bạn trong thư mục đã được đổi thành {role}.",
                relatedFolderId: folderId,
                targetUrl: $"/library/folders/{folderId}",
                metadata: new { folder_id = folderId, role });

            return Ok(new { success = true, member });
        }

        [HttpDelete("{folderId:int}/members/{memberUserId:guid}")]
        public async Task<IActionResult> RemoveMember(int folderId, Guid memberUserId)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            if (!await _permissionService.CanManageFolderMembersAsync(decodedToken.userID, folderId))
                return Forbid();

            var member = await _context.FOLDER_MEMBERS.FirstOrDefaultAsync(m => m.folder_id == folderId && m.user_id == memberUserId);
            if (member == null)
                return NotFound(new { success = false, message = "Không tìm thấy member." });

            _context.FOLDER_MEMBERS.Remove(member);
            await _context.SaveChangesAsync();

            await _notificationService.CreateAsync(
                recipientUserId: memberUserId,
                actorUserId: decodedToken.userID,
                type: "folder_member_removed",
                title: "Bạn đã bị xóa khỏi thư mục",
                message: "Bạn không còn là thành viên của thư mục này.",
                relatedFolderId: folderId,
                targetUrl: "/library?tab=shared",
                metadata: new { folder_id = folderId });

            return Ok(new { success = true, folder_id = folderId, removed_user_id = memberUserId });
        }

        [HttpDelete("{folderId:int}/members/me")]
        public async Task<IActionResult> LeaveFolder(int folderId)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            var folder = await _context.FOLDERS.AsNoTracking().FirstOrDefaultAsync(f => f.folder_id == folderId);
            if (folder == null)
                return NotFound(new { success = false, code = "FOLDER_NOT_FOUND", message = "Không tìm thấy thư mục." });
            if (folder.owner_user_id == decodedToken.userID)
                return BadRequest(new { success = false, message = "Owner không thể rời thư mục." });

            var member = await _context.FOLDER_MEMBERS.FirstOrDefaultAsync(m => m.folder_id == folderId && m.user_id == decodedToken.userID);
            if (member == null)
                return NotFound(new { success = false, message = "Bạn không phải thành viên thư mục này." });

            _context.FOLDER_MEMBERS.Remove(member);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, folder_id = folderId });
        }

        [HttpPost("{folderId:int}/invites")]
        public async Task<IActionResult> CreateInvite(int folderId, [FromBody] CreateFolderInviteDto dto)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            if (!await _permissionService.CanManageFolderMembersAsync(decodedToken.userID, folderId))
                return Forbid();

            var role = NormalizeRole(dto.role);
            if (role == null)
                return UnprocessableEntity(new { success = false, code = "INVALID_ROLE", message = "Role không hợp lệ." });
            if (!dto.invitee_user_id.HasValue && string.IsNullOrWhiteSpace(dto.invitee_email))
                return BadRequest(new { success = false, code = "VALIDATION_ERROR", message = "invitee_user_id hoặc invitee_email là bắt buộc." });

            var folder = await _context.FOLDERS.AsNoTracking().FirstOrDefaultAsync(f => f.folder_id == folderId);
            if (folder == null)
                return NotFound(new { success = false, code = "FOLDER_NOT_FOUND", message = "Không tìm thấy thư mục." });

            var inviteeEmail = dto.invitee_email?.Trim().ToLowerInvariant();
            Users? invitee = null;
            if (dto.invitee_user_id.HasValue)
            {
                invitee = await _context.USERS.AsNoTracking().FirstOrDefaultAsync(u => u.user_id == dto.invitee_user_id.Value);
                if (invitee == null)
                    return NotFound(new { success = false, message = "Không tìm thấy user được mời." });
            }
            else if (!string.IsNullOrWhiteSpace(inviteeEmail))
            {
                invitee = await _context.USERS.AsNoTracking().FirstOrDefaultAsync(u => u.Email.ToLower() == inviteeEmail);
            }

            var inviteeUserId = invitee?.user_id ?? dto.invitee_user_id;
            if (inviteeUserId == folder.owner_user_id)
                return Conflict(new { success = false, message = "Không thể mời owner vào thư mục của họ." });
            if (inviteeUserId.HasValue && await _context.FOLDER_MEMBERS.AnyAsync(m => m.folder_id == folderId && m.user_id == inviteeUserId.Value))
                return Conflict(new { success = false, code = "MEMBER_ALREADY_EXISTS", message = "User đã là thành viên." });

            var hasPendingInvite = await _context.FOLDER_INVITES.AnyAsync(i =>
                i.folder_id == folderId &&
                i.status == "pending" &&
                ((inviteeUserId.HasValue && i.invitee_user_id == inviteeUserId.Value) ||
                 (!string.IsNullOrWhiteSpace(inviteeEmail) && i.invitee_email == inviteeEmail)));

            if (hasPendingInvite)
                return Conflict(new { success = false, code = "INVITE_ALREADY_PENDING", message = "Đã có lời mời đang chờ." });

            FolderInvites? invite = null;
            var strategy = _context.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();

                invite = new FolderInvites
                {
                    folder_id = folderId,
                    inviter_user_id = decodedToken.userID,
                    invitee_user_id = inviteeUserId,
                    invitee_email = inviteeEmail,
                    role = role,
                    status = "pending",
                    token = GenerateInviteToken(),
                    expires_at = DateTime.UtcNow.AddDays(7),
                    created_at = DateTime.UtcNow,
                    updated_at = DateTime.UtcNow
                };

                _context.FOLDER_INVITES.Add(invite);
                await _context.SaveChangesAsync();

                if (inviteeUserId.HasValue)
                {
                    await _notificationService.CreateAsync(
                        recipientUserId: inviteeUserId.Value,
                        actorUserId: decodedToken.userID,
                        type: "folder_invite",
                        title: "Bạn được mời vào thư mục",
                        message: $"Bạn được mời vào thư mục \"{folder.name}\" với quyền {role}.",
                        relatedFolderId: folderId,
                        targetUrl: "/folder-invites",
                        metadata: new { folder_id = folderId, invite_id = invite.invite_id, role });
                }

                await transaction.CommitAsync();
            });

            return Ok(new { success = true, invite = ToInviteResponse(invite!) });
        }

        [HttpGet("{folderId:int}/invites")]
        public async Task<IActionResult> GetFolderInvites(int folderId, [FromQuery] PaginationParams paginationParams, [FromQuery] string? status = null)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            if (!await _permissionService.CanManageFolderMembersAsync(decodedToken.userID, folderId))
                return Forbid();

            var query = _context.FOLDER_INVITES.AsNoTracking().Where(i => i.folder_id == folderId);
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(i => i.status == status.Trim().ToLowerInvariant());

            var invites = await query
                .OrderByDescending(i => i.created_at)
                .Select(i => new
                {
                    i.invite_id,
                    i.folder_id,
                    i.inviter_user_id,
                    i.invitee_user_id,
                    i.invitee_email,
                    i.role,
                    i.status,
                    i.expires_at,
                    i.created_at,
                    i.updated_at
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(PagedResponse(invites));
        }

        [HttpGet("/api/folder-invites/my")]
        public async Task<IActionResult> GetMyInvites([FromQuery] PaginationParams paginationParams, [FromQuery] string? status = "pending")
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            var email = await _context.USERS
                .AsNoTracking()
                .Where(u => u.user_id == decodedToken.userID)
                .Select(u => u.Email.ToLower())
                .FirstOrDefaultAsync();

            var query = _context.FOLDER_INVITES
                .AsNoTracking()
                .Where(i => i.invitee_user_id == decodedToken.userID || (!string.IsNullOrEmpty(email) && i.invitee_email == email));

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(i => i.status == status.Trim().ToLowerInvariant());

            var invites = await query
                .OrderByDescending(i => i.created_at)
                .Select(i => new
                {
                    i.invite_id,
                    i.folder_id,
                    folder = i.Folder == null ? null : new
                    {
                        i.Folder.folder_id,
                        i.Folder.name,
                        i.Folder.description,
                        i.Folder.visibility
                    },
                    i.inviter_user_id,
                    inviter = i.InviterUser == null ? null : new
                    {
                        i.InviterUser.user_id,
                        i.InviterUser.Username,
                        i.InviterUser.full_name,
                        i.InviterUser.avatar_url
                    },
                    i.role,
                    i.status,
                    i.expires_at,
                    i.created_at
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(PagedResponse(invites));
        }

        [HttpPost("/api/folder-invites/{inviteId:int}/accept")]
        public async Task<IActionResult> AcceptInvite(int inviteId)
        {
            return await RespondToInvite(inviteId, accept: true);
        }

        [HttpPost("/api/folder-invites/{inviteId:int}/decline")]
        public async Task<IActionResult> DeclineInvite(int inviteId)
        {
            return await RespondToInvite(inviteId, accept: false);
        }

        [HttpPost("{folderId:int}/invites/{inviteId:int}/cancel")]
        public async Task<IActionResult> CancelInvite(int folderId, int inviteId)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            if (!await _permissionService.CanManageFolderMembersAsync(decodedToken.userID, folderId))
                return Forbid();

            var invite = await _context.FOLDER_INVITES.FirstOrDefaultAsync(i => i.invite_id == inviteId && i.folder_id == folderId);
            if (invite == null)
                return NotFound(new { success = false, message = "Không tìm thấy lời mời." });
            if (invite.status != "pending")
                return BadRequest(new { success = false, message = "Chỉ có thể hủy lời mời đang chờ." });

            invite.status = "cancelled";
            invite.updated_at = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, invite = ToInviteResponse(invite) });
        }

        private async Task<IActionResult> RespondToInvite(int inviteId, bool accept)
        {
            if (HttpContext.Items["DecodedToken"] is not DecodedTokenResponse decodedToken)
                return Unauthorized();

            var userEmail = await _context.USERS
                .AsNoTracking()
                .Where(u => u.user_id == decodedToken.userID)
                .Select(u => u.Email.ToLower())
                .FirstOrDefaultAsync();

            var invite = await _context.FOLDER_INVITES
                .Include(i => i.Folder)
                .FirstOrDefaultAsync(i => i.invite_id == inviteId);

            if (invite == null)
                return NotFound(new { success = false, message = "Không tìm thấy lời mời." });
            if (invite.invitee_user_id != decodedToken.userID && invite.invitee_email != userEmail)
                return Forbid();
            if (invite.status != "pending")
                return BadRequest(new { success = false, message = "Lời mời không còn ở trạng thái pending." });
            if (invite.expires_at.HasValue && invite.expires_at.Value <= DateTime.UtcNow)
            {
                invite.status = "expired";
                invite.updated_at = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return StatusCode(StatusCodes.Status410Gone, new { success = false, code = "INVITE_EXPIRED", message = "Lời mời đã hết hạn." });
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();

                invite.status = accept ? "accepted" : "declined";
                invite.updated_at = DateTime.UtcNow;

                if (accept && !await _context.FOLDER_MEMBERS.AnyAsync(m => m.folder_id == invite.folder_id && m.user_id == decodedToken.userID))
                {
                    _context.FOLDER_MEMBERS.Add(new FolderMembers
                    {
                        folder_id = invite.folder_id,
                        user_id = decodedToken.userID,
                        role = invite.role,
                        invited_by_user_id = invite.inviter_user_id
                    });
                }

                await _context.SaveChangesAsync();

                await _notificationService.CreateAsync(
                    recipientUserId: invite.inviter_user_id,
                    actorUserId: decodedToken.userID,
                    type: accept ? "folder_invite_accepted" : "folder_invite_declined",
                    title: accept ? "Lời mời thư mục đã được chấp nhận" : "Lời mời thư mục đã bị từ chối",
                    message: accept ? "Một người dùng đã chấp nhận lời mời vào thư mục." : "Một người dùng đã từ chối lời mời vào thư mục.",
                    relatedFolderId: invite.folder_id,
                    targetUrl: accept ? $"/library/folders/{invite.folder_id}" : $"/library/folders/{invite.folder_id}/invites",
                    metadata: new { folder_id = invite.folder_id, invite_id = invite.invite_id });

                await transaction.CommitAsync();
            });

            return Ok(new { success = true, invite = ToInviteResponse(invite) });
        }

        private async Task<bool> FolderNameExists(Guid ownerUserId, int? parentFolderId, string name, int? exceptFolderId)
        {
            return await _context.FOLDERS.AnyAsync(f =>
                f.owner_user_id == ownerUserId &&
                f.parent_folder_id == parentFolderId &&
                f.name == name &&
                (!exceptFolderId.HasValue || f.folder_id != exceptFolderId.Value));
        }

        private static string? NormalizeVisibility(string? visibility)
        {
            var normalized = string.IsNullOrWhiteSpace(visibility) ? "private" : visibility.Trim().ToLowerInvariant();
            return FolderVisibilities.Contains(normalized) ? normalized : null;
        }

        private static string? NormalizeRole(string role)
        {
            var normalized = role.Trim().ToLowerInvariant();
            return FolderRoles.Contains(normalized) ? normalized : null;
        }

        private static bool CanAccessDocument(Guid ownerId, bool isPublic, DecodedTokenResponse decodedToken)
        {
            return isPublic || ownerId == decodedToken.userID || decodedToken.roleID == "admin";
        }

        private static string GenerateInviteToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }

        private static object ToFolderResponse(Folders folder, string? role)
        {
            return new
            {
                folder.folder_id,
                folder.owner_user_id,
                folder.parent_folder_id,
                folder.name,
                folder.description,
                folder.visibility,
                folder.created_at,
                folder.updated_at,
                current_user_role = role
            };
        }

        private static object ToInviteResponse(FolderInvites invite)
        {
            return new
            {
                invite.invite_id,
                invite.folder_id,
                invite.inviter_user_id,
                invite.invitee_user_id,
                invite.invitee_email,
                invite.role,
                invite.status,
                invite.expires_at,
                invite.created_at,
                invite.updated_at
            };
        }

        private static object PermissionResponse(string? role)
        {
            return new
            {
                can_view = role != null,
                can_comment = role is "owner" or "admin" or "editor" or "contributor" or "commenter",
                can_add_document = role is "owner" or "admin" or "editor" or "contributor",
                can_remove_document = role is "owner" or "admin" or "editor",
                can_edit_folder = role is "owner" or "admin" or "editor",
                can_manage_members = role is "owner" or "admin",
                can_delete_folder = role == "owner"
            };
        }

        private static object PagedResponse<T>(PagedList<T> data)
        {
            return new
            {
                success = true,
                data,
                pagination = new
                {
                    data.CurrentPage,
                    data.PageSize,
                    data.TotalCount,
                    data.TotalPages
                }
            };
        }
    }
}
