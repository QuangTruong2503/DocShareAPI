using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using DocShareAPI.Controllers;
using DocShareAPI.Controllers.Public;
using DocShareAPI.Data;
using DocShareAPI.Helpers;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using DocShareAPI.Services;
using ELearningAPI.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace DocShareAPI.Tests;

public class BackendSecurityRegressionTests
{
    private static DocShareDbContext Database() => new(new DbContextOptionsBuilder<DocShareDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static T Authenticate<T>(T controller, Guid id, string role = "user") where T : ControllerBase { controller.ControllerContext = new() { HttpContext = new DefaultHttpContext() }; controller.HttpContext.Items["DecodedToken"] = new DecodedTokenResponse { userID = id, roleID = role }; return controller; }
    private static Users User(Guid id, string role = "user") => new() { user_id = id, Username = id.ToString(), Email = $"{id}@example.com", password_hash = "test", Role = role };
    private static Documents Document(int id, Guid owner, bool shared = false) => new() { document_id = id, user_id = owner, Title = "Test.pdf", is_public = shared, public_id = $"doc-{id}", asset_id = $"asset-{id}", file_url = $"https://res.cloudinary.com/test/image/authenticated/v1/doc-{id}.pdf", thumbnail_url = "", file_size = 100, pages = 1 };
    private static Tokens Token(Guid id, Guid user, TokenType type = TokenType.TwoFactorLogin) => new() { token_id = id, user_id = user, token = id.ToString(), type = type, is_active = true, expires_at = DateTime.UtcNow.AddMinutes(5) };
    private static JsonElement Body(IActionResult result) => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static AssetDelivery Delivery(DocShareDbContext db) => new(db, new FolderPermissionService(db), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["TokenSecretKey"] = new string('x',64) }).Build());

    [Fact]
    public void JwtIsUniqueAndTemporaryTokensHavePurposeAndShortLifetime()
    {
        var service = new TokenServices(new string('x',64));
        Assert.Equal(500, Enumerable.Range(0,500).Select(_ => service.GenerateToken("user","admin")).Distinct().Count());
        var temporary = new JwtSecurityTokenHandler().ReadJwtToken(service.GenerateToken("user","user","TwoFactorLogin"));
        Assert.Equal("TwoFactorLogin",temporary.Claims.Single(x=>x.Type=="purpose").Value);
        Assert.True(temporary.ValidTo < DateTime.UtcNow.AddMinutes(6));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("YQ==")]
    public void MalformedPasswordHashesFailSafely(string hash) => Assert.False(PasswordHasher.VerifyPassword("password",hash));

    [Fact]
    public async Task FavoritesRecheckRevokedPublicAndFolderAccessBeforePagination()
    {
        await using var db=Database(); var viewer=Guid.NewGuid(); var owner=Guid.NewGuid(); db.USERS.AddRange(User(viewer),User(owner));
        var document=Document(1,owner,true); db.DOCUMENTS.Add(document); db.FOLDERS.Add(new Folders { folder_id=1,owner_user_id=owner,name="Private" });
        db.FOLDER_DOCUMENTS.Add(new FolderDocuments {folder_id=1,document_id=1,added_by_user_id=owner}); db.FOLDER_MEMBERS.Add(new FolderMembers {folder_id=1,user_id=viewer,role="viewer"});
        db.FAVORITES.Add(new Favorites {user_id=viewer,item_type="document",item_id=1}); await db.SaveChangesAsync();
        var controller=Authenticate(new FavoritesController(db,new FolderPermissionService(db),new AuditLogService(db)),viewer);
        Assert.Single(Body(await controller.GetFavorites(new PaginationParams())).GetProperty("items").EnumerateArray());
        document.is_public=false; db.FOLDER_MEMBERS.RemoveRange(db.FOLDER_MEMBERS); await db.SaveChangesAsync();
        var result=Body(await controller.GetFavorites(new PaginationParams())); Assert.Empty(result.GetProperty("items").EnumerateArray()); Assert.Equal(0,result.GetProperty("pagination").GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(null)]
    public async Task MovingTrackedFolderLinkReplacesTheCompositeKey(int? target)
    {
        await using var db=Database(); var owner=Guid.NewGuid(); db.FOLDER_DOCUMENTS.Add(new FolderDocuments {folder_id=1,document_id=1,added_by_user_id=owner}); await db.SaveChangesAsync();
        await DocumentFolderLinks.MoveAsync(db,1,target,owner); await db.SaveChangesAsync();
        Assert.Equal(target,await db.FOLDER_DOCUMENTS.Where(x=>x.document_id==1).Select(x=>(int?)x.folder_id).FirstOrDefaultAsync());
    }

    [Fact]
    public async Task NonOwnerCannotMovePublicRootDocument()
    {
        await using var db=Database(); var viewer=Guid.NewGuid(); db.DOCUMENTS.Add(Document(1,Guid.NewGuid(),true)); await db.SaveChangesAsync();
        var controller=Authenticate(new FoldersController(db,new FolderPermissionService(db),null!),viewer);
        Assert.IsType<ForbidResult>(await controller.MoveDocumentToFolder(1,new DocShareAPI.DataTransferObject.Folders.MoveDocumentFolderDto {target_folder_id=2}));
        Assert.Empty(db.FOLDER_DOCUMENTS);
    }

    [Theory]
    [InlineData("viewer",false)]
    [InlineData("commenter",true)]
    [InlineData("contributor",true)]
    [InlineData("editor",true)]
    [InlineData("admin",true)]
    public async Task PrivateDocumentCommentsUseCommentCapability(string role,bool allowed)
    {
        await using var db=Database(); var owner=Guid.NewGuid();var member=Guid.NewGuid();db.USERS.AddRange(User(owner),User(member)); db.DOCUMENTS.Add(Document(1,owner));
        db.FOLDERS.Add(new Folders {folder_id=1,name="Folder",owner_user_id=owner});db.FOLDER_DOCUMENTS.Add(new FolderDocuments {folder_id=1,document_id=1,added_by_user_id=owner});db.FOLDER_MEMBERS.Add(new FolderMembers {folder_id=1,user_id=member,role=role});await db.SaveChangesAsync();
        var controller=Authenticate(new CommentsController(db,new FolderPermissionService(db),new NotificationService(db,null!),new AuditLogService(db)),member);
        var result=await controller.CreateComment(1,new CommentRequest {content="Hello"});if(allowed)Assert.IsType<OkObjectResult>(result);else Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task TrashIsUnavailableThroughPublicDetailAndHistory()
    {
        await using var db=Database();var owner=Guid.NewGuid();db.USERS.Add(User(owner));var doc=Document(1,owner,true);doc.deleted_at=DateTime.UtcNow;db.DOCUMENTS.Add(doc);await db.SaveChangesAsync();
        var controller=Authenticate(new PublicDocumentsController(db,new FolderPermissionService(db)),owner);
        Assert.IsType<NotFoundObjectResult>(await controller.GetDocumentByID(1));
        var history=Body(await controller.GetHistoryDocuments(["1"])); Assert.DoesNotContain("file_url",history.GetRawText());
    }

    [Fact]
    public async Task PasswordResetRevokesEveryActiveSessionAndChallenge()
    {
        await using var db=Database();var id=Guid.NewGuid();db.USERS.Add(User(id));var reset=Token(Guid.NewGuid(),id,TokenType.PasswordReset); reset.token=TokenHasher.HashToken("reset");db.TOKENS.AddRange(reset,Token(Guid.NewGuid(),id,TokenType.Access),Token(Guid.NewGuid(),id));await db.SaveChangesAsync();
        var controller=new VerificationController(db,null!,null!,null!,null!);
        Assert.IsType<OkObjectResult>(await controller.ResetPassword(new VerificationController.ResetPasswordRequest {token="reset",newPassword="new-password"}));Assert.All(db.TOKENS,t=>Assert.False(t.is_active));
    }

    [Fact]
    public async Task TwoFactorChallengeHasSharedAttemptBudgetCooldownAndSingleUse()
    {
        await using var db=Database();var user=Guid.NewGuid();var first=Guid.NewGuid();var second=Guid.NewGuid();db.TOKENS.AddRange(Token(first,user),Token(second,user));await db.SaveChangesAsync();
        Assert.True(await TwoFactorChallenges.SaveAsync(db,first,"123456"));Assert.True(await TwoFactorChallenges.SaveAsync(db,second,"654321"));
        Assert.False(await TwoFactorChallenges.SaveAsync(db,first,"222222",true));
        Assert.False(await TwoFactorChallenges.VerifyAsync(db,second,"123456"));Assert.True(await TwoFactorChallenges.VerifyAsync(db,first,"123456"));Assert.False(await TwoFactorChallenges.VerifyAsync(db,first,"123456"));
        for(var i=0;i<4;i++)Assert.False(await TwoFactorChallenges.VerifyAsync(db,second,"wrong"));Assert.False(await TwoFactorChallenges.VerifyAsync(db,second,"654321"));
    }

    [Fact]
    public async Task AvatarPatchUsesExplicitNullAndDoesNotChangeOtherFields()
    {
        await using var db=Database();var admin=Guid.NewGuid();var user=User(Guid.NewGuid());user.avatar_url="avatar";user.full_name="Original";db.USERS.AddRange(User(admin,"admin"),user);await db.SaveChangesAsync();
        var request=JsonSerializer.Deserialize<AdminController.AdminUpdateUserRequest>("{\"avatarUrl\":null}",new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var controller=Authenticate(new AdminController(db,null!,new NotificationService(db,null!)),admin,"admin");
        Assert.IsType<OkObjectResult>(await controller.UpdateUser(user.user_id,request));Assert.Null(user.avatar_url);Assert.Equal("Original",user.full_name);Assert.Equal("user",user.Role);
        Assert.Contains(db.AUDIT_LOGS,a=>a.action=="user.updated");
    }

    [Fact]
    public async Task AdminCannotDemoteOrDeleteSelfAndRoleChangesRevokeSessions()
    {
        await using var db=Database();var admin=Guid.NewGuid();var other=Guid.NewGuid();db.USERS.AddRange(User(admin,"admin"),User(other,"admin"));db.TOKENS.Add(Token(Guid.NewGuid(),other,TokenType.Access));await db.SaveChangesAsync();
        var controller=Authenticate(new AdminController(db,null!,new NotificationService(db,null!)),admin,"admin");
        Assert.IsType<BadRequestObjectResult>(await controller.UpdateUser(admin,new() {Role="user"}));Assert.IsType<BadRequestObjectResult>(await controller.DeleteUser(admin));
        Assert.IsType<OkObjectResult>(await controller.UpdateUser(other,new() {Role="user"}));Assert.False(db.TOKENS.Single().is_active);
    }

    [Fact]
    public async Task ReportModerationSavesDocumentReportAuditAndNotificationTogether()
    {
        await using var db=Database();var admin=Guid.NewGuid();var reporter=Guid.NewGuid();var owner=Guid.NewGuid();db.USERS.AddRange(User(admin,"admin"),User(reporter),User(owner));db.DOCUMENTS.Add(Document(1,owner,true));db.REPORTS.Add(new Reports {report_id=1,document_id=1,user_id=reporter,Reason="Violation",Status="Chờ giải quyết"});await db.SaveChangesAsync();
        var controller=Authenticate(new AdminController(db,null!,new NotificationService(db,null!)),admin,"admin");Assert.IsType<OkObjectResult>(await controller.ResolveReport(1,new() {Action="hide_document",Note="Moderated"}));
        Assert.False(db.DOCUMENTS.Single().is_public);Assert.Equal("Đã xử lý",db.REPORTS.Single().Status);Assert.Contains(db.AUDIT_LOGS,a=>a.action=="report.resolved");Assert.Contains(db.NOTIFICATIONS,n=>n.recipient_user_id==reporter);
    }

    [Fact]
    public async Task StorageIncludesTrashAndDistinctHistoricalAssets()
    {
        await using var db=Database();var user=Guid.NewGuid();var doc=Document(1,user);doc.deleted_at=DateTime.UtcNow;db.DOCUMENTS.Add(doc);db.DOCUMENT_VERSIONS.AddRange(new DocumentVersions {document_id=1,version_number=1,file_url="old",public_id="old",asset_id="old",file_size=50},new DocumentVersions {document_id=1,version_number=2,file_url="old",public_id="old",asset_id="old",file_size=50});await db.SaveChangesAsync();
        Assert.Equal(150,await StorageAccounting.UsedAsync(db,user));
    }

    [Fact]
    public async Task GatewayGrantCannotBeTamperedAndRevocationTakesEffectImmediately()
    {
        await using var db=Database();var owner=Guid.NewGuid();db.USERS.Add(User(owner));var doc=Document(1,owner);db.DOCUMENTS.Add(doc);var token=Token(Guid.NewGuid(),owner,TokenType.Access);token.token=TokenHasher.HashToken("access");db.TOKENS.Add(token);await db.SaveChangesAsync();
        var context=new DefaultHttpContext();context.Request.Host=new HostString("localhost");context.Request.Headers.Authorization="Bearer access";context.Items["DecodedToken"]=new DecodedTokenResponse {userID=owner,roleID="user"};var service=Delivery(db);var signed=service.Issue(context,1).Split("?grant=")[1];var grant=service.Read(signed)!;
        Assert.Null(service.Read(signed+"x"));Assert.True(await service.CanRead(doc,grant));token.is_active=false;await db.SaveChangesAsync();Assert.False(await service.CanRead(doc,grant));
        var link=new ShareLinks {token="share",item_id=1,item_type="document",owner_user_id=owner,access="anyone_with_link",permission="viewer"};db.SHARE_LINKS.Add(link);await db.SaveChangesAsync();var shared=service.Read(service.Issue(context,1,share:link).Split("?grant=")[1])!;Assert.True(await service.CanRead(doc,shared));link.revoked_at=DateTime.UtcNow;await db.SaveChangesAsync();Assert.False(await service.CanRead(doc,shared));
    }
}
