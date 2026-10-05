using System.Text.Json;
using DocShareAPI.Controllers;
using DocShareAPI.Controllers.Public;
using DocShareAPI.Data;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace DocShareAPI.Tests;

public class ShareLinkRedesignTests
{
    private static DocShareDbContext Database() => new(new DbContextOptionsBuilder<DocShareDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static T Context<T>(T controller, Guid? user, string role = "user") where T : ControllerBase
    {
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext() };
        if (user.HasValue) controller.HttpContext.Items["DecodedToken"] = new DecodedTokenResponse { userID = user.Value, roleID = role };
        return controller;
    }
    private static ShareLinksController Shares(DocShareDbContext db, Guid user, string role = "user")
        => Context(new ShareLinksController(db, new ConfigurationBuilder().Build(), new AuditLogService(db)), user, role);
    private static JsonElement Body(ObjectResult result) => JsonSerializer.SerializeToElement(result.Value);
    private static Documents Document(Guid owner) => new() { document_id = 1, user_id = owner, Title = "Document",
        is_public = true, file_url = "fixture", thumbnail_url = "", public_id = "fixture", asset_id = "fixture", pages = 1 };
    private static ShareLinkRequest Request() => new() { itemId = 1, itemType = "document", access = "anyone_with_link", permission = "viewer" };

    [Theory]
    [InlineData("owner", true)]
    [InlineData("ADMIN", true)]
    [InlineData("other", false)]
    [InlineData("guest", false)]
    public async Task DocumentDetailReportsSharePermission(string viewer, bool expected)
    {
        await using var db = Database(); var owner = Guid.NewGuid();
        db.USERS.Add(new Users { user_id = owner, Username = "owner", Email = "owner@example.com", password_hash = "fixture" });
        db.DOCUMENTS.Add(Document(owner)); await db.SaveChangesAsync();
        var controller = Context(new PublicDocumentsController(db, new FolderPermissionService(db)),
            viewer == "guest" ? null : viewer == "owner" ? owner : Guid.NewGuid(), viewer);
        var response = Assert.IsType<OkObjectResult>(await controller.GetDocumentByID(1));
        Assert.Equal(expected, Body(response).GetProperty("can_share").GetBoolean());
        if (viewer == "ADMIN")
            Assert.IsType<OkObjectResult>(await Shares(db, Guid.NewGuid(), viewer).CreateOrUpdateShareLink(Request()));
    }

    [Fact]
    public async Task NonOwnerGetsMeaningfulForbiddenResponse()
    {
        await using var db = Database(); db.DOCUMENTS.Add(Document(Guid.NewGuid())); await db.SaveChangesAsync();
        var response = Assert.IsType<ObjectResult>(await Shares(db, Guid.NewGuid()).CreateOrUpdateShareLink(Request()));
        Assert.Equal(403, response.StatusCode); Assert.Equal("SHARE_FORBIDDEN", Body(response).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task ExpirationIsNormalizedAndResponsesSerializeUtc(DateTimeKind kind)
    {
        await using var db = Database(); var owner = Guid.NewGuid(); db.DOCUMENTS.Add(Document(owner)); await db.SaveChangesAsync();
        var input = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(7), kind);
        var request = Request(); request.expiresAt = input;
        var controller = Shares(db, owner);
        Assert.IsType<OkObjectResult>(await controller.CreateOrUpdateShareLink(request));
        var link = await db.SHARE_LINKS.SingleAsync();
        Assert.Equal(kind == DateTimeKind.Local ? input.ToUniversalTime() : input, link.expires_at);
        Assert.Equal(DateTimeKind.Utc, link.expires_at!.Value.Kind);
        link.expires_at = DateTime.SpecifyKind(link.expires_at.Value, DateTimeKind.Unspecified);
        link.created_at = DateTime.SpecifyKind(link.created_at, DateTimeKind.Unspecified);
        link.updated_at = DateTime.SpecifyKind(link.updated_at, DateTimeKind.Unspecified);
        await db.SaveChangesAsync();
        var body = Body(Assert.IsType<OkObjectResult>(await controller.GetShareLink(1, "document"))).GetProperty("shareLink");
        foreach (var field in new[] { "expiresAt", "createdAt", "updatedAt" })
        {
            Assert.EndsWith("Z", body.GetProperty(field).GetString());
            Assert.Equal(DateTimeKind.Utc, body.GetProperty(field).GetDateTime().Kind);
        }
    }

    [Fact]
    public async Task PastExpirationIsRejected()
    {
        await using var db = Database(); var owner = Guid.NewGuid(); db.DOCUMENTS.Add(Document(owner)); await db.SaveChangesAsync();
        var request = Request(); request.expiresAt = DateTime.UtcNow.AddMinutes(-1);
        var response = Assert.IsType<BadRequestObjectResult>(await Shares(db, owner).CreateOrUpdateShareLink(request));
        Assert.Equal("INVALID_LIMIT", Body(response).GetProperty("code").GetString()); Assert.Empty(db.SHARE_LINKS);
    }

    [Fact]
    public async Task PasswordCanBeSetPreservedAndRemovedWithoutChangingUrl()
    {
        await using var db = Database(); var owner = Guid.NewGuid(); db.DOCUMENTS.Add(Document(owner)); await db.SaveChangesAsync();
        var controller = Shares(db, owner); var request = Request(); request.password = "abc";
        var first = Body(Assert.IsType<OkObjectResult>(await controller.CreateOrUpdateShareLink(request))).GetProperty("shareLink");
        Assert.True(first.GetProperty("requiresPassword").GetBoolean()); var url = first.GetProperty("shareUrl").GetString();
        request.password = null;
        var kept = Body(Assert.IsType<OkObjectResult>(await controller.CreateOrUpdateShareLink(request))).GetProperty("shareLink");
        Assert.True(kept.GetProperty("requiresPassword").GetBoolean()); Assert.Equal(url, kept.GetProperty("shareUrl").GetString());
        request.password = "";
        var cleared = Body(Assert.IsType<OkObjectResult>(await controller.CreateOrUpdateShareLink(request))).GetProperty("shareLink");
        Assert.False(cleared.GetProperty("requiresPassword").GetBoolean()); Assert.Equal(url, cleared.GetProperty("shareUrl").GetString());
    }

    [Fact]
    public async Task RestrictedLinkRemainsUnavailableToPublicClients()
    {
        await using var db = Database(); var owner = Guid.NewGuid(); db.DOCUMENTS.Add(Document(owner)); await db.SaveChangesAsync();
        var controller = Shares(db, owner); var request = Request(); request.access = "restricted";
        Assert.IsType<OkObjectResult>(await controller.CreateOrUpdateShareLink(request));
        Assert.IsType<NotFoundObjectResult>(await controller.GetPublicShare((await db.SHARE_LINKS.SingleAsync()).token));
    }
}
