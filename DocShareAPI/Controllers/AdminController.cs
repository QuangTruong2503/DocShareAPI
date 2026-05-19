using CloudinaryDotNet.Actions;
using DocShareAPI.Data;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DocShareAPI.Controllers
{
    [Route("api/admin")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly INotificationService _notificationService;

        public AdminController(DocShareDbContext context, ICloudinaryService cloudinaryService, INotificationService notificationService)
        {
            _context = context;
            _cloudinaryService = cloudinaryService;
            _notificationService = notificationService;
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var now = DateTime.UtcNow;
            var last7Days = now.AddDays(-7);
            var last30Days = now.AddDays(-30);

            var totalUsers = await _context.USERS.CountAsync();
            var totalDocuments = await _context.DOCUMENTS.CountAsync();
            var totalPublicDocuments = await _context.DOCUMENTS.CountAsync(d => d.is_public);
            var totalPrivateDocuments = totalDocuments - totalPublicDocuments;
            var totalReports = await _context.REPORTS.CountAsync();
            var pendingReports = await _context.REPORTS.CountAsync(r => r.Status == "Chờ giải quyết");
            var totalDownloads = await _context.DOCUMENTS.SumAsync(d => (int?)d.download_count) ?? 0;
            var totalCollections = await _context.COLLECTIONS.CountAsync();

            var recentDocuments = await _context.DOCUMENTS
                .AsNoTracking()
                .OrderByDescending(d => d.uploaded_at)
                .Take(5)
                .Select(d => new
                {
                    d.document_id,
                    d.Title,
                    d.thumbnail_url,
                    d.is_public,
                    d.uploaded_at,
                    owner = d.Users == null ? null : new
                    {
                        d.Users.user_id,
                        d.Users.Username,
                        d.Users.full_name,
                        d.Users.avatar_url
                    }
                })
                .ToListAsync();

            var recentReports = await _context.REPORTS
                .AsNoTracking()
                .OrderByDescending(r => r.created_at)
                .Take(5)
                .Select(r => new
                {
                    r.report_id,
                    r.Reason,
                    r.Status,
                    r.created_at,
                    reporter = new
                    {
                        r.Users.user_id,
                        r.Users.Username,
                        r.Users.full_name
                    },
                    document = new
                    {
                        r.Documents.document_id,
                        r.Documents.Title
                    }
                })
                .ToListAsync();

            var documentUploadTrend = await _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.uploaded_at >= last7Days)
                .GroupBy(d => d.uploaded_at.Date)
                .Select(g => new { date = g.Key, count = g.Count() })
                .OrderBy(g => g.date)
                .ToListAsync();

            return Ok(new
            {
                success = true,
                data = new
                {
                    totals = new
                    {
                        users = totalUsers,
                        documents = totalDocuments,
                        publicDocuments = totalPublicDocuments,
                        privateDocuments = totalPrivateDocuments,
                        reports = totalReports,
                        pendingReports,
                        downloads = totalDownloads,
                        collections = totalCollections
                    },
                    last30Days = new
                    {
                        newUsers = await _context.USERS.CountAsync(u => u.created_at >= last30Days),
                        newDocuments = await _context.DOCUMENTS.CountAsync(d => d.uploaded_at >= last30Days),
                        newReports = await _context.REPORTS.CountAsync(r => r.created_at >= last30Days)
                    },
                    recentDocuments,
                    recentReports,
                    documentUploadTrend = documentUploadTrend.Select(x => new
                    {
                        date = x.date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        x.count
                    })
                }
            });
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers(
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] string? search,
            [FromQuery] string? role,
            [FromQuery] bool? isVerified,
            [FromQuery] string sortBy = "created_at",
            [FromQuery] string sortDirection = "desc")
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var query = _context.USERS.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalizedSearch = search.Trim().ToLower();
                query = query.Where(u =>
                    u.Username.ToLower().Contains(normalizedSearch) ||
                    u.Email.ToLower().Contains(normalizedSearch) ||
                    (u.full_name != null && u.full_name.ToLower().Contains(normalizedSearch)));
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                var normalizedRole = role.Trim().ToLower();
                query = query.Where(u => u.Role.ToLower() == normalizedRole);
            }

            if (isVerified.HasValue)
                query = query.Where(u => u.is_verified == isVerified.Value);

            query = ApplyUserSort(query, sortBy, sortDirection);

            var users = await query
                .Select(u => new
                {
                    u.user_id,
                    u.Username,
                    u.Email,
                    u.full_name,
                    u.avatar_url,
                    u.created_at,
                    u.Role,
                    u.is_verified,
                    u.two_factor_enabled,
                    document_count = u.Documents == null ? 0 : u.Documents.Count,
                    collection_count = u.Collections == null ? 0 : u.Collections.Count,
                    follower_count = u.Followers == null ? 0 : u.Followers.Count,
                    following_count = u.Following == null ? 0 : u.Following.Count
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(ToPagedResponse(users));
        }

        [HttpGet("users/{userId:guid}")]
        public async Task<IActionResult> GetUserDetail(Guid userId)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var user = await _context.USERS
                .AsNoTracking()
                .Where(u => u.user_id == userId)
                .Select(u => new
                {
                    u.user_id,
                    u.Username,
                    u.Email,
                    u.full_name,
                    u.avatar_url,
                    u.avatar_public_id,
                    u.created_at,
                    u.Role,
                    u.is_verified,
                    u.two_factor_enabled,
                    two_factor_method = u.two_factor_method == null ? null : u.two_factor_method.ToString(),
                    u.two_factor_verified_at,
                    stats = new
                    {
                        documents = u.Documents == null ? 0 : u.Documents.Count,
                        publicDocuments = u.Documents == null ? 0 : u.Documents.Count(d => d.is_public),
                        collections = u.Collections == null ? 0 : u.Collections.Count,
                        likes = u.Likes == null ? 0 : u.Likes.Count(l => l.reaction == 1),
                        dislikes = u.Likes == null ? 0 : u.Likes.Count(l => l.reaction == -1),
                        followers = u.Followers == null ? 0 : u.Followers.Count,
                        following = u.Following == null ? 0 : u.Following.Count
                    }
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return NotFound(new { success = false, message = "Không tìm thấy người dùng." });

            var recentDocuments = await _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.user_id == userId)
                .OrderByDescending(d => d.uploaded_at)
                .Take(10)
                .Select(d => new
                {
                    d.document_id,
                    d.Title,
                    d.thumbnail_url,
                    d.is_public,
                    d.uploaded_at,
                    d.download_count
                })
                .ToListAsync();

            return Ok(new
            {
                success = true,
                data = new
                {
                    user,
                    recentDocuments
                }
            });
        }

        [HttpPatch("users/{userId:guid}")]
        public async Task<IActionResult> UpdateUser(Guid userId, [FromBody] AdminUpdateUserRequest request)
        {
            if (!TryRequireAdmin(out var error, out var adminToken))
                return error!;

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == userId);
            if (user == null)
                return NotFound(new { success = false, message = "Không tìm thấy người dùng." });

            var changes = new List<string>();

            if (request.Role != null)
            {
                var role = request.Role.Trim().ToLowerInvariant();
                if (role is not ("admin" or "user"))
                    return BadRequest(new { success = false, message = "Vai trò phải là admin hoặc user." });

                if (user.user_id == adminToken!.userID && role != "admin")
                    return BadRequest(new { success = false, message = "Quản trị viên không thể tự gỡ quyền admin của chính mình." });

                if (user.Role != role)
                {
                    user.Role = role;
                    changes.Add("role");
                }
            }

            if (request.FullName != null)
            {
                var fullName = string.IsNullOrWhiteSpace(request.FullName) ? null : request.FullName.Trim();
                if (user.full_name != fullName)
                {
                    user.full_name = fullName;
                    changes.Add("full_name");
                }
            }

            if (request.IsVerified.HasValue)
            {
                if (user.is_verified != request.IsVerified.Value)
                {
                    user.is_verified = request.IsVerified.Value;
                    changes.Add("is_verified");
                }
            }

            if (request.TwoFactorEnabled.HasValue)
            {
                if (user.two_factor_enabled != request.TwoFactorEnabled.Value)
                {
                    user.two_factor_enabled = request.TwoFactorEnabled.Value;
                    changes.Add("two_factor_enabled");
                }
            }

            await _context.SaveChangesAsync();

            if (changes.Count > 0 && userId != adminToken!.userID)
            {
                await _notificationService.CreateAsync(
                    recipientUserId: userId,
                    actorUserId: adminToken.userID,
                    type: "ACCOUNT_UPDATED_BY_ADMIN",
                    title: "Tài khoản của bạn đã được cập nhật",
                    message: "Quản trị viên vừa cập nhật thông tin tài khoản của bạn.",
                    targetUrl: "/profile",
                    metadata: new { changes });
            }

            return Ok(new
            {
                success = true,
                message = "Cập nhật người dùng thành công.",
                data = new
                {
                    user.user_id,
                    user.Username,
                    user.Email,
                    user.full_name,
                    user.avatar_url,
                    user.Role,
                    user.is_verified,
                    user.two_factor_enabled
                }
            });
        }

        [HttpDelete("users/{userId:guid}")]
        public async Task<IActionResult> DeleteUser(Guid userId)
        {
            if (!TryRequireAdmin(out var error, out var adminToken))
                return error!;

            if (adminToken!.userID == userId)
                return BadRequest(new { success = false, message = "Quản trị viên không thể tự xóa tài khoản của chính mình." });

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == userId);
            if (user == null)
                return NotFound(new { success = false, message = "Không tìm thấy người dùng." });

            var userDocumentIds = await _context.DOCUMENTS
                .Where(d => d.user_id == userId)
                .Select(d => d.document_id)
                .ToListAsync();

            await DeleteDocumentRelations(userDocumentIds);

            var userCollectionIds = await _context.COLLECTIONS
                .Where(c => c.user_id == userId)
                .Select(c => c.collection_id)
                .ToListAsync();

            await _context.COLLECTION_DOCUMENTS
                .Where(cd => userCollectionIds.Contains(cd.collection_id))
                .ExecuteDeleteAsync();
            await _context.COLLECTIONS
                .Where(c => c.user_id == userId)
                .ExecuteDeleteAsync();
            await _context.TOKENS
                .Where(t => t.user_id == userId)
                .ExecuteDeleteAsync();
            await _context.LIKES
                .Where(l => l.user_id == userId)
                .ExecuteDeleteAsync();
            await _context.FOLLOWS
                .Where(f => f.follower_id == userId || f.following_id == userId)
                .ExecuteDeleteAsync();
            await _context.REPORTS
                .Where(r => r.user_id == userId)
                .ExecuteDeleteAsync();
            await _context.DOCUMENTS
                .Where(d => d.user_id == userId)
                .ExecuteDeleteAsync();

            _context.USERS.Remove(user);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Xóa người dùng thành công.",
                deletedUserId = userId
            });
        }

        [HttpGet("documents")]
        public async Task<IActionResult> GetDocuments(
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] string? search,
            [FromQuery] Guid? userId,
            [FromQuery] bool? isPublic,
            [FromQuery] string? categoryId,
            [FromQuery] string? tagId,
            [FromQuery] string sortBy = "uploaded_at",
            [FromQuery] string sortDirection = "desc")
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var query = _context.DOCUMENTS.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalizedSearch = search.Trim().ToLower();
                query = query.Where(d =>
                    d.Title.ToLower().Contains(normalizedSearch) ||
                    (d.Description != null && d.Description.ToLower().Contains(normalizedSearch)) ||
                    (d.Users != null && d.Users.Email.ToLower().Contains(normalizedSearch)) ||
                    (d.Users != null && d.Users.Username.ToLower().Contains(normalizedSearch)));
            }

            if (userId.HasValue)
                query = query.Where(d => d.user_id == userId.Value);

            if (isPublic.HasValue)
                query = query.Where(d => d.is_public == isPublic.Value);

            if (!string.IsNullOrWhiteSpace(categoryId))
                query = query.Where(d => d.DocumentCategories.Any(dc => dc.category_id == categoryId));

            if (!string.IsNullOrWhiteSpace(tagId))
                query = query.Where(d => d.DocumentTags != null && d.DocumentTags.Any(dt => dt.tag_id == tagId));

            query = ApplyDocumentSort(query, sortBy, sortDirection);

            var documents = await query
                .Select(d => new
                {
                    d.document_id,
                    d.user_id,
                    owner = d.Users == null ? null : new
                    {
                        d.Users.user_id,
                        d.Users.Username,
                        d.Users.full_name,
                        d.Users.Email,
                        d.Users.avatar_url
                    },
                    d.Title,
                    d.Description,
                    d.thumbnail_url,
                    d.file_url,
                    d.file_type,
                    d.file_size,
                    d.pages,
                    d.is_public,
                    d.uploaded_at,
                    d.download_count,
                    like_count = d.Likes == null ? 0 : d.Likes.Count(l => l.reaction == 1),
                    dislike_count = d.Likes == null ? 0 : d.Likes.Count(l => l.reaction == -1),
                    report_count = _context.REPORTS.Count(r => r.document_id == d.document_id)
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(ToPagedResponse(documents));
        }

        [HttpGet("documents/{documentId:int}")]
        public async Task<IActionResult> GetDocumentDetail(int documentId)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var document = await _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.document_id == documentId)
                .Select(d => new
                {
                    d.document_id,
                    d.user_id,
                    owner = d.Users == null ? null : new
                    {
                        d.Users.user_id,
                        d.Users.Username,
                        d.Users.full_name,
                        d.Users.Email,
                        d.Users.avatar_url
                    },
                    d.Title,
                    d.Description,
                    d.public_id,
                    d.asset_id,
                    d.file_url,
                    d.thumbnail_url,
                    d.download_count,
                    d.uploaded_at,
                    d.file_type,
                    d.file_size,
                    d.pages,
                    d.is_public,
                    categories = d.DocumentCategories.Select(dc => new
                    {
                        dc.Categories.category_id,
                        dc.Categories.Name,
                        dc.Categories.Description,
                        dc.Categories.parent_id
                    }),
                    tags = d.DocumentTags == null ? Enumerable.Empty<object>() : d.DocumentTags.Select(dt => new
                    {
                        dt.Tags!.tag_id,
                        dt.Tags.Name
                    }),
                    reactions = new
                    {
                        likes = d.Likes == null ? 0 : d.Likes.Count(l => l.reaction == 1),
                        dislikes = d.Likes == null ? 0 : d.Likes.Count(l => l.reaction == -1)
                    }
                })
                .FirstOrDefaultAsync();

            if (document == null)
                return NotFound(new { success = false, message = "Không tìm thấy tài liệu." });

            var reports = await _context.REPORTS
                .AsNoTracking()
                .Where(r => r.document_id == documentId)
                .OrderByDescending(r => r.created_at)
                .Select(r => new
                {
                    r.report_id,
                    r.Reason,
                    r.Status,
                    r.created_at,
                    reporter = new
                    {
                        r.Users.user_id,
                        r.Users.Username,
                        r.Users.full_name,
                        r.Users.Email
                    }
                })
                .ToListAsync();

            return Ok(new
            {
                success = true,
                data = new
                {
                    document,
                    reports
                }
            });
        }

        [HttpPatch("documents/{documentId:int}")]
        public async Task<IActionResult> UpdateDocument(int documentId, [FromBody] AdminUpdateDocumentRequest request)
        {
            if (!TryRequireAdmin(out var error, out var adminToken))
                return error!;

            var document = await _context.DOCUMENTS
                .Include(d => d.DocumentCategories)
                .Include(d => d.DocumentTags)
                .FirstOrDefaultAsync(d => d.document_id == documentId);

            if (document == null)
                return NotFound(new { success = false, message = "Không tìm thấy tài liệu." });

            var changes = new List<string>();

            if (request.Title != null)
            {
                if (string.IsNullOrWhiteSpace(request.Title))
                    return BadRequest(new { success = false, message = "Tiêu đề không được để trống." });

                var title = request.Title.Trim();
                if (document.Title != title)
                {
                    document.Title = title;
                    changes.Add("title");
                }
            }

            if (request.Description != null)
            {
                var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                if (document.Description != description)
                {
                    document.Description = description;
                    changes.Add("description");
                }
            }

            if (request.IsPublic.HasValue)
            {
                if (document.is_public != request.IsPublic.Value)
                {
                    document.is_public = request.IsPublic.Value;
                    changes.Add("is_public");
                }
            }

            if (request.CategoryIds != null)
            {
                var categoryIds = request.CategoryIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct()
                    .ToList();

                var existingCategoryIds = await _context.CATEGORIES
                    .Where(c => categoryIds.Contains(c.category_id))
                    .Select(c => c.category_id)
                    .ToListAsync();

                var currentCategoryIds = document.DocumentCategories
                    .Select(dc => dc.category_id)
                    .ToList();

                var categoriesToRemove = document.DocumentCategories
                    .Where(dc => !existingCategoryIds.Contains(dc.category_id))
                    .ToList();

                _context.DOCUMENT_CATEGORIES.RemoveRange(categoriesToRemove);

                var categoriesToAdd = existingCategoryIds
                    .Where(categoryId => !currentCategoryIds.Contains(categoryId))
                    .Select(categoryId => new DocumentCategories
                    {
                        document_id = documentId,
                        category_id = categoryId
                    });

                await _context.DOCUMENT_CATEGORIES.AddRangeAsync(categoriesToAdd);
                changes.Add("categories");
            }

            if (request.Tags != null)
            {
                var tagNames = request.Tags
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var tagMap = tagNames
                    .Select(name => new { Name = name, Id = NormalizeId(name) })
                    .Where(t => !string.IsNullOrWhiteSpace(t.Id))
                    .GroupBy(t => t.Id)
                    .Select(g => g.First())
                    .ToList();

                var tagIds = tagMap.Select(t => t.Id).ToList();
                var existingTagIds = await _context.TAGS
                    .Where(t => tagIds.Contains(t.tag_id))
                    .Select(t => t.tag_id)
                    .ToListAsync();

                var newTags = tagMap
                    .Where(t => !existingTagIds.Contains(t.Id))
                    .Select(t => new Tags { tag_id = t.Id, Name = t.Name })
                    .ToList();

                await _context.TAGS.AddRangeAsync(newTags);

                var currentTagIds = (document.DocumentTags ?? Enumerable.Empty<DocumentTags>())
                    .Select(dt => dt.tag_id)
                    .ToList();

                var tagsToRemove = (document.DocumentTags ?? Enumerable.Empty<DocumentTags>())
                    .Where(dt => !tagIds.Contains(dt.tag_id))
                    .ToList();

                _context.DOCUMENT_TAGS.RemoveRange(tagsToRemove);

                var tagsToAdd = tagIds
                    .Where(tagId => !currentTagIds.Contains(tagId))
                    .Select(tagId => new DocumentTags
                    {
                        document_id = documentId,
                        tag_id = tagId
                    });

                await _context.DOCUMENT_TAGS.AddRangeAsync(tagsToAdd);
                changes.Add("tags");
            }

            await _context.SaveChangesAsync();

            if (changes.Count > 0 && document.user_id != adminToken!.userID)
            {
                await _notificationService.CreateAsync(
                    recipientUserId: document.user_id,
                    actorUserId: adminToken.userID,
                    type: "DOCUMENT_UPDATED_BY_ADMIN",
                    title: "Tài liệu của bạn đã được cập nhật",
                    message: $"Quản trị viên vừa cập nhật tài liệu \"{document.Title}\".",
                    relatedDocumentId: document.document_id,
                    targetUrl: $"/documents/{document.document_id}",
                    metadata: new { changes = changes.Distinct().ToList() });
            }

            return Ok(new
            {
                success = true,
                message = "Cập nhật tài liệu thành công.",
                document_id = documentId
            });
        }

        [HttpDelete("documents/{documentId:int}")]
        public async Task<IActionResult> DeleteDocument(int documentId)
        {
            if (!TryRequireAdmin(out var error, out var adminToken))
                return error!;

            var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentId);
            if (document == null)
                return NotFound(new { success = false, message = "Không tìm thấy tài liệu." });

            var ownerId = document.user_id;
            var documentTitle = document.Title;

            await DeleteDocumentRelations(new[] { documentId });

            if (!string.IsNullOrWhiteSpace(document.public_id))
            {
                try
                {
                    await _cloudinaryService.Cloudinary.DeleteResourcesAsync(new DelResParams
                    {
                        PublicIds = new List<string> { document.public_id },
                        Type = "upload"
                    });
                }
                catch
                {
                    // Keep admin moderation usable even if remote asset cleanup fails.
                }
            }

            _context.DOCUMENTS.Remove(document);
            await _context.SaveChangesAsync();

            if (ownerId != adminToken!.userID)
            {
                await _notificationService.CreateAsync(
                    recipientUserId: ownerId,
                    actorUserId: adminToken.userID,
                    type: "DOCUMENT_DELETED_BY_ADMIN",
                    title: "Tài liệu của bạn đã bị xóa",
                    message: $"Quản trị viên đã xóa tài liệu \"{documentTitle}\".",
                    metadata: new { document_id = documentId, title = documentTitle });
            }

            return Ok(new
            {
                success = true,
                message = "Xóa tài liệu thành công.",
                deletedDocumentId = documentId
            });
        }

        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories([FromQuery] string? search)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var query = _context.CATEGORIES.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalizedSearch = search.Trim().ToLower();
                query = query.Where(c =>
                    c.category_id.ToLower().Contains(normalizedSearch) ||
                    c.Name.ToLower().Contains(normalizedSearch) ||
                    (c.Description != null && c.Description.ToLower().Contains(normalizedSearch)));
            }

            var categories = await query
                .OrderBy(c => c.parent_id)
                .ThenBy(c => c.Name)
                .Select(c => new
                {
                    c.category_id,
                    c.Name,
                    c.Description,
                    c.parent_id,
                    document_count = c.DocumentCategories.Count,
                    child_count = _context.CATEGORIES.Count(child => child.parent_id == c.category_id)
                })
                .ToListAsync();

            return Ok(new { success = true, data = categories });
        }

        [HttpPost("categories")]
        public async Task<IActionResult> CreateCategory([FromBody] AdminCategoryRequest request)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { success = false, message = "Tên là bắt buộc." });

            var categoryId = string.IsNullOrWhiteSpace(request.CategoryId)
                ? NormalizeId(request.Name)
                : NormalizeId(request.CategoryId);

            if (string.IsNullOrWhiteSpace(categoryId))
                return BadRequest(new { success = false, message = "ID danh mục không hợp lệ." });

            if (await _context.CATEGORIES.AnyAsync(c => c.category_id == categoryId))
                return Conflict(new { success = false, message = "Danh mục đã tồn tại." });

            if (!string.IsNullOrWhiteSpace(request.ParentId) &&
                !await _context.CATEGORIES.AnyAsync(c => c.category_id == request.ParentId))
            {
                return BadRequest(new { success = false, message = "Danh mục cha không tồn tại." });
            }

            var category = new Categories
            {
                category_id = categoryId,
                Name = request.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                parent_id = string.IsNullOrWhiteSpace(request.ParentId) ? null : request.ParentId.Trim()
            };

            _context.CATEGORIES.Add(category);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetCategories), new
            {
                success = true,
                message = "Tạo danh mục thành công.",
                data = category
            });
        }

        [HttpPatch("categories/{categoryId}")]
        public async Task<IActionResult> UpdateCategory(string categoryId, [FromBody] AdminCategoryRequest request)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var category = await _context.CATEGORIES.FirstOrDefaultAsync(c => c.category_id == categoryId);
            if (category == null)
                return NotFound(new { success = false, message = "Không tìm thấy danh mục." });

            if (request.Name != null)
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                    return BadRequest(new { success = false, message = "Tên không được để trống." });

                category.Name = request.Name.Trim();
            }

            if (request.Description != null)
                category.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

            if (request.ParentId != null)
            {
                var parentId = string.IsNullOrWhiteSpace(request.ParentId) ? null : request.ParentId.Trim();
                if (parentId == categoryId)
                    return BadRequest(new { success = false, message = "Danh mục không thể là danh mục cha của chính nó." });

                if (parentId != null && !await _context.CATEGORIES.AnyAsync(c => c.category_id == parentId))
                    return BadRequest(new { success = false, message = "Danh mục cha không tồn tại." });

                category.parent_id = parentId;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Cập nhật danh mục thành công.",
                data = category
            });
        }

        [HttpDelete("categories/{categoryId}")]
        public async Task<IActionResult> DeleteCategory(string categoryId)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var category = await _context.CATEGORIES.FirstOrDefaultAsync(c => c.category_id == categoryId);
            if (category == null)
                return NotFound(new { success = false, message = "Không tìm thấy danh mục." });

            await _context.DOCUMENT_CATEGORIES
                .Where(dc => dc.category_id == categoryId)
                .ExecuteDeleteAsync();

            var children = await _context.CATEGORIES
                .Where(c => c.parent_id == categoryId)
                .ToListAsync();

            foreach (var child in children)
                child.parent_id = null;

            _context.CATEGORIES.Remove(category);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Xóa danh mục thành công.",
                deletedCategoryId = categoryId
            });
        }

        [HttpGet("tags")]
        public async Task<IActionResult> GetTags([FromQuery] string? search)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var query = _context.TAGS.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalizedSearch = search.Trim().ToLower();
                query = query.Where(t =>
                    t.tag_id.ToLower().Contains(normalizedSearch) ||
                    t.Name.ToLower().Contains(normalizedSearch));
            }

            var tags = await query
                .OrderBy(t => t.Name)
                .Select(t => new
                {
                    t.tag_id,
                    t.Name,
                    document_count = t.DocumentTags == null ? 0 : t.DocumentTags.Count
                })
                .ToListAsync();

            return Ok(new { success = true, data = tags });
        }

        [HttpPost("tags")]
        public async Task<IActionResult> CreateTag([FromBody] AdminTagRequest request)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { success = false, message = "Tên là bắt buộc." });

            var tagId = string.IsNullOrWhiteSpace(request.TagId)
                ? NormalizeId(request.Name)
                : NormalizeId(request.TagId);

            if (await _context.TAGS.AnyAsync(t => t.tag_id == tagId))
                return Conflict(new { success = false, message = "Thẻ đã tồn tại." });

            var tag = new Tags
            {
                tag_id = tagId,
                Name = request.Name.Trim()
            };

            _context.TAGS.Add(tag);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetTags), new
            {
                success = true,
                message = "Tạo thẻ thành công.",
                data = tag
            });
        }

        [HttpPatch("tags/{tagId}")]
        public async Task<IActionResult> UpdateTag(string tagId, [FromBody] AdminTagRequest request)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var tag = await _context.TAGS.FirstOrDefaultAsync(t => t.tag_id == tagId);
            if (tag == null)
                return NotFound(new { success = false, message = "Không tìm thấy thẻ." });

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { success = false, message = "Tên là bắt buộc." });

            tag.Name = request.Name.Trim();
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Cập nhật thẻ thành công.",
                data = tag
            });
        }

        [HttpDelete("tags/{tagId}")]
        public async Task<IActionResult> DeleteTag(string tagId)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var tag = await _context.TAGS.FirstOrDefaultAsync(t => t.tag_id == tagId);
            if (tag == null)
                return NotFound(new { success = false, message = "Không tìm thấy thẻ." });

            await _context.DOCUMENT_TAGS
                .Where(dt => dt.tag_id == tagId)
                .ExecuteDeleteAsync();

            _context.TAGS.Remove(tag);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Xóa thẻ thành công.",
                deletedTagId = tagId
            });
        }

        [HttpGet("reports")]
        public async Task<IActionResult> GetReports(
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] string? status,
            [FromQuery] int? documentId,
            [FromQuery] Guid? userId)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var query = _context.REPORTS.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(r => r.Status == status);

            if (documentId.HasValue)
                query = query.Where(r => r.document_id == documentId.Value);

            if (userId.HasValue)
                query = query.Where(r => r.user_id == userId.Value);

            var reports = await query
                .OrderByDescending(r => r.created_at)
                .Select(r => new
                {
                    r.report_id,
                    r.user_id,
                    r.document_id,
                    r.Reason,
                    r.Status,
                    r.created_at,
                    reporter = new
                    {
                        r.Users.user_id,
                        r.Users.Username,
                        r.Users.full_name,
                        r.Users.Email
                    },
                    document = new
                    {
                        r.Documents.document_id,
                        r.Documents.Title,
                        r.Documents.thumbnail_url,
                        r.Documents.is_public
                    }
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(ToPagedResponse(reports));
        }

        [HttpGet("reports/{reportId:int}")]
        public async Task<IActionResult> GetReportDetail(int reportId)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var report = await _context.REPORTS
                .AsNoTracking()
                .Where(r => r.report_id == reportId)
                .Select(r => new
                {
                    r.report_id,
                    r.user_id,
                    r.document_id,
                    r.Reason,
                    r.Status,
                    r.created_at,
                    reporter = new
                    {
                        r.Users.user_id,
                        r.Users.Username,
                        r.Users.full_name,
                        r.Users.Email,
                        r.Users.avatar_url
                    },
                    document = new
                    {
                        r.Documents.document_id,
                        r.Documents.Title,
                        r.Documents.Description,
                        r.Documents.thumbnail_url,
                        r.Documents.file_url,
                        r.Documents.is_public,
                        r.Documents.uploaded_at,
                        r.Documents.download_count
                    }
                })
                .FirstOrDefaultAsync();

            if (report == null)
                return NotFound(new { success = false, message = "Không tìm thấy báo cáo." });

            return Ok(new { success = true, data = report });
        }

        [HttpPatch("reports/{reportId:int}")]
        public async Task<IActionResult> UpdateReportStatus(int reportId, [FromBody] AdminUpdateReportRequest request)
        {
            if (!TryRequireAdmin(out var error, out var adminToken))
                return error!;

            if (string.IsNullOrWhiteSpace(request.Status))
                return BadRequest(new { success = false, message = "Trạng thái là bắt buộc." });

            var allowedStatuses = new[] { "Chờ giải quyết", "Đang xử lý", "Đã xử lý", "Từ chối" };
            if (!allowedStatuses.Contains(request.Status))
                return BadRequest(new { success = false, message = "Trạng thái không hợp lệ." });

            var report = await _context.REPORTS
                .Include(r => r.Documents)
                .FirstOrDefaultAsync(r => r.report_id == reportId);
            if (report == null)
                return NotFound(new { success = false, message = "Không tìm thấy báo cáo." });

            var oldStatus = report.Status;
            report.Status = request.Status;
            await _context.SaveChangesAsync();

            if (oldStatus != report.Status && report.user_id != adminToken!.userID)
            {
                await _notificationService.CreateAsync(
                    recipientUserId: report.user_id,
                    actorUserId: adminToken.userID,
                    type: "REPORT_STATUS_UPDATED",
                    title: "Báo cáo của bạn đã được cập nhật",
                    message: $"Báo cáo cho tài liệu \"{report.Documents.Title}\" đã chuyển sang trạng thái \"{report.Status}\".",
                    relatedDocumentId: report.document_id,
                    relatedReportId: report.report_id,
                    targetUrl: $"/reports/{report.report_id}",
                    metadata: new { old_status = oldStatus, new_status = report.Status });
            }

            return Ok(new
            {
                success = true,
                message = "Cập nhật trạng thái báo cáo thành công.",
                data = new
                {
                    report.report_id,
                    report.Status
                }
            });
        }

        [HttpDelete("reports/{reportId:int}")]
        public async Task<IActionResult> DeleteReport(int reportId)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var report = await _context.REPORTS.FirstOrDefaultAsync(r => r.report_id == reportId);
            if (report == null)
                return NotFound(new { success = false, message = "Không tìm thấy báo cáo." });

            _context.REPORTS.Remove(report);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Xóa báo cáo thành công.",
                deletedReportId = reportId
            });
        }

        [HttpGet("collections")]
        public async Task<IActionResult> GetCollections(
            [FromQuery] PaginationParams paginationParams,
            [FromQuery] string? search,
            [FromQuery] Guid? userId,
            [FromQuery] bool? isPublic)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var query = _context.COLLECTIONS.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalizedSearch = search.Trim().ToLower();
                query = query.Where(c =>
                    c.Name.ToLower().Contains(normalizedSearch) ||
                    (c.Description != null && c.Description.ToLower().Contains(normalizedSearch)));
            }

            if (userId.HasValue)
                query = query.Where(c => c.user_id == userId.Value);

            if (isPublic.HasValue)
                query = query.Where(c => c.is_public == isPublic.Value);

            var collections = await query
                .OrderByDescending(c => c.created_at)
                .Select(c => new
                {
                    c.collection_id,
                    c.user_id,
                    owner = c.Users == null ? null : new
                    {
                        c.Users.user_id,
                        c.Users.Username,
                        c.Users.full_name,
                        c.Users.Email,
                        c.Users.avatar_url
                    },
                    c.Name,
                    c.Description,
                    c.is_public,
                    c.created_at,
                    document_count = c.CollectionDocuments == null ? 0 : c.CollectionDocuments.Count
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(ToPagedResponse(collections));
        }

        [HttpGet("collections/{collectionId:int}")]
        public async Task<IActionResult> GetCollectionDetail(int collectionId)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var collection = await _context.COLLECTIONS
                .AsNoTracking()
                .Where(c => c.collection_id == collectionId)
                .Select(c => new
                {
                    c.collection_id,
                    c.user_id,
                    owner = c.Users == null ? null : new
                    {
                        c.Users.user_id,
                        c.Users.Username,
                        c.Users.full_name,
                        c.Users.Email,
                        c.Users.avatar_url
                    },
                    c.Name,
                    c.Description,
                    c.is_public,
                    c.created_at
                })
                .FirstOrDefaultAsync();

            if (collection == null)
                return NotFound(new { success = false, message = "Không tìm thấy bộ sưu tập." });

            var documents = await _context.COLLECTION_DOCUMENTS
                .AsNoTracking()
                .Where(cd => cd.collection_id == collectionId)
                .OrderByDescending(cd => cd.added_at)
                .Select(cd => new
                {
                    cd.document_id,
                    cd.added_at,
                    cd.Documents!.Title,
                    cd.Documents.Description,
                    cd.Documents.thumbnail_url,
                    cd.Documents.is_public,
                    cd.Documents.uploaded_at,
                    cd.Documents.download_count
                })
                .ToListAsync();

            return Ok(new
            {
                success = true,
                data = new
                {
                    collection,
                    document_count = documents.Count,
                    documents
                }
            });
        }

        [HttpDelete("collections/{collectionId:int}")]
        public async Task<IActionResult> DeleteCollection(int collectionId)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var collection = await _context.COLLECTIONS.FirstOrDefaultAsync(c => c.collection_id == collectionId);
            if (collection == null)
                return NotFound(new { success = false, message = "Không tìm thấy bộ sưu tập." });

            await _context.COLLECTION_DOCUMENTS
                .Where(cd => cd.collection_id == collectionId)
                .ExecuteDeleteAsync();

            _context.COLLECTIONS.Remove(collection);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Xóa bộ sưu tập thành công.",
                deletedCollectionId = collectionId
            });
        }

        [HttpGet("analytics/documents")]
        public async Task<IActionResult> GetDocumentAnalytics([FromQuery] int days = 30)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            days = days <= 0 ? 30 : Math.Min(days, 365);
            var startDate = DateTime.UtcNow.Date.AddDays(-(days - 1));

            var uploads = await _context.DOCUMENTS
                .AsNoTracking()
                .Where(d => d.uploaded_at >= startDate)
                .GroupBy(d => d.uploaded_at.Date)
                .Select(g => new
                {
                    date = g.Key,
                    count = g.Count()
                })
                .OrderBy(g => g.date)
                .ToListAsync();

            var topDocuments = await _context.DOCUMENTS
                .AsNoTracking()
                .OrderByDescending(d => d.download_count)
                .Take(10)
                .Select(d => new
                {
                    d.document_id,
                    d.Title,
                    d.thumbnail_url,
                    d.download_count,
                    d.is_public,
                    owner = d.Users == null ? null : new
                    {
                        d.Users.user_id,
                        d.Users.Username,
                        d.Users.full_name
                    }
                })
                .ToListAsync();

            var categoryDistribution = await _context.DOCUMENT_CATEGORIES
                .AsNoTracking()
                .GroupBy(dc => new { dc.category_id, dc.Categories.Name })
                .Select(g => new
                {
                    g.Key.category_id,
                    g.Key.Name,
                    document_count = g.Count()
                })
                .OrderByDescending(g => g.document_count)
                .Take(10)
                .ToListAsync();

            return Ok(new
            {
                success = true,
                data = new
                {
                    days,
                    uploads = uploads.Select(u => new
                    {
                        date = u.date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        u.count
                    }),
                    topDocuments,
                    categoryDistribution
                }
            });
        }

        private bool TryRequireAdmin(out IActionResult? error)
        {
            return TryRequireAdmin(out error, out _);
        }

        private bool TryRequireAdmin(out IActionResult? error, out DecodedTokenResponse? decodedToken)
        {
            decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                error = Unauthorized(new { success = false, message = "Bạn cần đăng nhập." });
                return false;
            }

            if (!string.Equals(decodedToken.roleID, "admin", StringComparison.OrdinalIgnoreCase))
            {
                error = Forbid();
                return false;
            }

            error = null;
            return true;
        }

        private async Task DeleteDocumentRelations(IEnumerable<int> documentIds)
        {
            var ids = documentIds.Distinct().ToList();
            if (ids.Count == 0)
                return;

            await _context.DOCUMENT_CATEGORIES.Where(dc => ids.Contains(dc.document_id)).ExecuteDeleteAsync();
            await _context.DOCUMENT_TAGS.Where(dt => ids.Contains(dt.document_id)).ExecuteDeleteAsync();
            await _context.COLLECTION_DOCUMENTS.Where(cd => ids.Contains(cd.document_id)).ExecuteDeleteAsync();
            await _context.LIKES.Where(l => ids.Contains(l.document_id)).ExecuteDeleteAsync();
            await _context.REPORTS.Where(r => ids.Contains(r.document_id)).ExecuteDeleteAsync();
        }

        private static object ToPagedResponse<T>(PagedList<T> pagedData)
        {
            return new
            {
                success = true,
                data = pagedData,
                pagination = new
                {
                    pagedData.CurrentPage,
                    pagedData.PageSize,
                    pagedData.TotalCount,
                    pagedData.TotalPages
                }
            };
        }

        private static IQueryable<Users> ApplyUserSort(IQueryable<Users> query, string sortBy, string sortDirection)
        {
            var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
            return sortBy.ToLowerInvariant() switch
            {
                "username" => descending ? query.OrderByDescending(u => u.Username) : query.OrderBy(u => u.Username),
                "email" => descending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
                "role" => descending ? query.OrderByDescending(u => u.Role) : query.OrderBy(u => u.Role),
                _ => descending ? query.OrderByDescending(u => u.created_at) : query.OrderBy(u => u.created_at)
            };
        }

        private static IQueryable<Documents> ApplyDocumentSort(IQueryable<Documents> query, string sortBy, string sortDirection)
        {
            var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
            return sortBy.ToLowerInvariant() switch
            {
                "title" => descending ? query.OrderByDescending(d => d.Title) : query.OrderBy(d => d.Title),
                "download_count" => descending ? query.OrderByDescending(d => d.download_count) : query.OrderBy(d => d.download_count),
                "file_size" => descending ? query.OrderByDescending(d => d.file_size) : query.OrderBy(d => d.file_size),
                _ => descending ? query.OrderByDescending(d => d.uploaded_at) : query.OrderBy(d => d.uploaded_at)
            };
        }

        private static string NormalizeId(string value)
        {
            var normalized = value.Trim().Normalize(NormalizationForm.FormD);
            var withoutDiacritics = new string(normalized
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray())
                .Normalize(NormalizationForm.FormC)
                .ToLowerInvariant();

            return Regex.Replace(withoutDiacritics, @"[^a-z0-9]+", "-").Trim('-');
        }

        public class AdminUpdateUserRequest
        {
            public string? FullName { get; set; }
            public string? Role { get; set; }
            public bool? IsVerified { get; set; }
            public bool? TwoFactorEnabled { get; set; }
        }

        public class AdminUpdateDocumentRequest
        {
            public string? Title { get; set; }
            public string? Description { get; set; }
            public bool? IsPublic { get; set; }
            public ICollection<string>? CategoryIds { get; set; }
            public ICollection<string>? Tags { get; set; }
        }

        public class AdminCategoryRequest
        {
            public string? CategoryId { get; set; }
            public string? Name { get; set; }
            public string? Description { get; set; }
            public string? ParentId { get; set; }
        }

        public class AdminTagRequest
        {
            public string? TagId { get; set; }
            public string? Name { get; set; }
        }

        public class AdminUpdateReportRequest
        {
            public string? Status { get; set; }
        }
    }
}
