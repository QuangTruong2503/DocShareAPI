using System.Text.Json;
using DocShareAPI.Controllers;
using DocShareAPI.Data;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DocShareAPI.Tests;

public class LibraryRegressionTests
{
    private static DocShareDbContext Database() => new(new DbContextOptionsBuilder<DocShareDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static T Authenticate<T>(T controller, Guid user) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Items["DecodedToken"] = new DecodedTokenResponse { userID = user, roleID = "user" };
        return controller;
    }
    private static JsonElement Body(IActionResult result) => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);
    private static Users User(Guid id) => new() { user_id = id, Username = id.ToString(), Email = $"{id}@example.com", password_hash = "test" };
    private static Documents Document(int id, Guid user, string name, string extension = "pdf") => new() {
        document_id = id, user_id = user, Title = name, file_type = extension,
        public_id = $"doc-{id}", asset_id = $"asset-{id}", file_url = "https://example.com/file", thumbnail_url = "", pages = 1, file_size = 100
    };

    [Fact]
    public async Task FolderSortingHappensBeforePaginationAndTotalIncludesAllPages()
    {
        await using var db = Database(); var user = Guid.NewGuid();
        db.USERS.Add(User(user));
        db.FOLDERS.Add(new Folders { folder_id = 1, owner_user_id = user, name = "Z Folder", updated_at = DateTime.UtcNow.AddDays(-10) });
        db.DOCUMENTS.AddRange(Enumerable.Range(1, 51).Select(id => Document(id, user, $"A File {id}")));
        await db.SaveChangesAsync();
        var controller = Authenticate(new LibraryController(db, new FolderPermissionService(db)), user);
        var response = Body(await controller.GetMyLibrary(new PaginationParams { PageNumber = 1, PageSize = 50 }, sort: "name_asc"));
        Assert.Equal(52, response.GetProperty("pagination").GetProperty("totalCount").GetInt32());
        Assert.Equal(50, response.GetProperty("items").GetArrayLength());
        Assert.Equal("folder", response.GetProperty("items")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task SharedBreadcrumbDoesNotExposeAnInaccessibleParent()
    {
        await using var db = Database(); var user = Guid.NewGuid(); var owner = Guid.NewGuid();
        db.USERS.AddRange(User(user), User(owner));
        db.FOLDERS.AddRange(new Folders { folder_id = 10, owner_user_id = owner, name = "Secret parent" },
            new Folders { folder_id = 11, parent_folder_id = 10, owner_user_id = owner, name = "Shared child" });
        db.FOLDER_MEMBERS.Add(new FolderMembers { folder_id = 11, user_id = user, role = "viewer" });
        await db.SaveChangesAsync();
        var controller = Authenticate(new LibraryController(db, new FolderPermissionService(db)), user);
        var response = Body(await controller.GetFolderItems(11, new PaginationParams()));
        var folder = response.GetProperty("folder");
        Assert.Equal("shared", folder.GetProperty("rootArea").GetString());
        Assert.Single(folder.GetProperty("breadcrumb").EnumerateArray());
        Assert.DoesNotContain("Secret parent", folder.GetRawText());
        Assert.Equal("/library/folders/11", folder.GetProperty("breadcrumb")[0].GetProperty("href").GetString());
    }

    [Fact]
    public async Task SharedTreeIncludesRootsWhoseParentsAreInaccessibleAndRespectsViewerPermissions()
    {
        await using var db = Database(); var user = Guid.NewGuid(); var owner = Guid.NewGuid();
        db.FOLDERS.AddRange(new Folders { folder_id = 10, owner_user_id = owner, name = "Hidden" },
            new Folders { folder_id = 11, parent_folder_id = 10, owner_user_id = owner, name = "Shared" });
        db.FOLDER_MEMBERS.Add(new FolderMembers { folder_id = 11, user_id = user, role = "viewer" });
        await db.SaveChangesAsync();
        var controller = Authenticate(new FoldersController(db, new FolderPermissionService(db), null!), user);
        var nodes = Body(await controller.GetFolderTree()).GetProperty("nodes");
        Assert.Single(nodes.EnumerateArray());
        Assert.Equal("shared", nodes[0].GetProperty("rootArea").GetString());
        Assert.False(nodes[0].GetProperty("canReceiveItems").GetBoolean());
    }

    [Fact]
    public async Task MergeRejectsAnotherUsersDocumentWithoutCreatingAFolder()
    {
        await using var db = Database(); var user = Guid.NewGuid();
        db.DOCUMENTS.AddRange(Document(1, user, "Mine"), Document(2, Guid.NewGuid(), "Other"));
        await db.SaveChangesAsync();
        var controller = Authenticate(new LibraryItemsController(db, new FolderPermissionService(db), null!), user);
        var response = await controller.MergeIntoFolder(new MergeFolderRequest { name = "Merged", items = [new() { id = 1, type = "document" }, new() { id = 2, type = "document" }] });
        Assert.Equal(403, Assert.IsType<ObjectResult>(response).StatusCode);
        Assert.Empty(await db.FOLDERS.ToListAsync());
        Assert.Empty(await db.FOLDER_DOCUMENTS.ToListAsync());
    }

    [Fact]
    public async Task DeletedItemsCannotBeFavorited()
    {
        await using var db = Database(); var user = Guid.NewGuid();
        var document = Document(1, user, "Deleted"); document.deleted_at = DateTime.UtcNow;
        db.DOCUMENTS.Add(document); await db.SaveChangesAsync();
        var controller = Authenticate(new FavoritesController(db, new FolderPermissionService(db), null!), user);
        Assert.IsType<NotFoundObjectResult>(await controller.SetFavorite(1, new FavoriteRequest { type = "document", favorite = true }));
        Assert.Empty(await db.FAVORITES.ToListAsync());
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(false, 11)]
    [InlineData(true, 10)]
    [InlineData(true, 11)]
    public async Task MovingOrCopyingFolderIntoItselfOrDescendantIsRejected(bool copy, int destination)
    {
        await using var db = Database(); var user = Guid.NewGuid();
        db.USERS.Add(User(user));
        db.FOLDERS.AddRange(new Folders { folder_id = 10, owner_user_id = user, name = "Parent" }, new Folders { folder_id = 11, parent_folder_id = 10, owner_user_id = user, name = "Child" });
        await db.SaveChangesAsync();
        var controller = Authenticate(new LibraryItemsController(db, new FolderPermissionService(db), null!), user);
        var request = new MoveLibraryItemsRequest { targetFolderId = destination, items = [new() { id = 10, type = "folder" }] };
        var response = Body(copy ? await controller.CopyItems(request) : await controller.MoveItems(request));
        Assert.Single(response.GetProperty("failed").EnumerateArray());
        Assert.Null((await db.FOLDERS.FindAsync(10))!.parent_folder_id);
        Assert.Equal(2, await db.FOLDERS.CountAsync());
    }

    [Fact]
    public async Task ImageFilterIncludesDifferentImageExtensionsAndInvalidSortFallsBack()
    {
        await using var db = Database(); var user = Guid.NewGuid();
        db.USERS.Add(User(user));
        db.DOCUMENTS.AddRange(Document(1, user, "One.png", "png"), Document(2, user, "Two.jpeg", "jpeg"), Document(3, user, "Three.pdf"));
        await db.SaveChangesAsync();
        var controller = Authenticate(new LibraryController(db, new FolderPermissionService(db)), user);
        var response = Body(await controller.GetMyLibrary(new PaginationParams(), sort: "invalid", fileType: "image"));
        Assert.Equal(2, response.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task SummaryCountsActiveFavoritesAndRootTrashSeparately()
    {
        await using var db = Database(); var user = Guid.NewGuid();
        var active = Document(1, user, "Active"); var deleted = Document(2, user, "Deleted");
        deleted.deleted_at = DateTime.UtcNow; deleted.deleted_root_type = "document"; deleted.deleted_root_id = 2;
        db.DOCUMENTS.AddRange(active, deleted);
        db.FAVORITES.AddRange(new Favorites { user_id = user, item_id = 1, item_type = "document" }, new Favorites { user_id = user, item_id = 2, item_type = "document" });
        await db.SaveChangesAsync();
        var controller = Authenticate(new LibraryController(db, new FolderPermissionService(db)), user);
        var counts = Body(await controller.GetSummary()).GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("my").GetInt32());
        Assert.Equal(1, counts.GetProperty("favorites").GetInt32());
        Assert.Equal(1, counts.GetProperty("trash").GetInt32());
    }
}
