using System.Text.Json;
using DocShareAPI.Controllers;
using DocShareAPI.Data;
using DocShareAPI.Helpers;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocShareAPI.Tests;

public class RelationalRegressionTests
{
    [Fact]
    public async Task GoogleIdentityIsBoundOnlyAfterItsOwnTwoFactorChallengeSucceeds()
    {
        await using var db=await Database(); var id=Guid.NewGuid(); var user=User(id); user.two_factor_enabled=true; db.USERS.Add(user);
        var challenge=new Tokens {token_id=Guid.NewGuid(),user_id=id,token=TokenHasher.HashToken("temporary"),type=TokenType.TwoFactorLogin,is_active=true,expires_at=DateTime.UtcNow.AddMinutes(5)};
        db.TOKENS.Add(challenge); await db.SaveChangesAsync();
        Assert.True(await TwoFactorChallenges.SaveAsync(db,challenge.token_id,"123456",googleSubject:"google-subject"));
        using var services=new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var controller=new UsersController(db,null!,Microsoft.Extensions.Logging.Abstractions.NullLogger<UsersController>.Instance,new TokenServices(new string('x',64)),new ConfigurationBuilder().Build(),services.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),null!);
        Assert.IsType<OkObjectResult>(await controller.Verify2FA(new(){TempToken="temporary",Code="wrong"}));
        Assert.Empty(await db.EXTERNAL_IDENTITIES.ToListAsync()); Assert.Empty(await db.TOKENS.Where(t=>t.type==TokenType.Access).ToListAsync());
        Assert.IsType<OkObjectResult>(await controller.Verify2FA(new(){TempToken="temporary",Code="123456"}));
        Assert.Equal("google-subject",(await db.EXTERNAL_IDENTITIES.SingleAsync()).subject); Assert.Single(await db.TOKENS.Where(t=>t.type==TokenType.Access).ToListAsync());
        await controller.Verify2FA(new(){TempToken="temporary",Code="123456"}); Assert.Single(await db.EXTERNAL_IDENTITIES.ToListAsync()); Assert.Single(await db.TOKENS.Where(t=>t.type==TokenType.Access).ToListAsync());
    }

    [Fact]
    public async Task UserDeletionHandlesTrackedBulkDeletedDependents()
    {
        await using var db = await Database(); var admin = Guid.NewGuid(); var owner = Guid.NewGuid();
        db.USERS.AddRange(User(admin,"admin"),User(owner)); db.DOCUMENTS.Add(Document(1,owner));
        db.COLLECTIONS.Add(new Collections { collection_id=1, user_id=owner, Name="Saved" });
        db.COLLECTION_DOCUMENTS.Add(new CollectionDocuments {collection_id=1, document_id=1});
        db.TOKENS.Add(new Tokens {token_id=Guid.NewGuid(),user_id=owner,token="test",type=TokenType.Access});
        db.EXTERNAL_IDENTITIES.Add(new ExternalIdentity {provider="google",subject="subject",user_id=owner});
        await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await Admin(db,admin).DeleteUser(owner)); db.ChangeTracker.Clear();
        Assert.Single(await db.USERS.ToListAsync()); Assert.Empty(await db.DOCUMENTS.ToListAsync());
        Assert.Empty(await db.EXTERNAL_IDENTITIES.ToListAsync()); Assert.Contains(await db.AUDIT_LOGS.ToListAsync(),a=>a.action=="asset.cleanup.pending");
    }

    [Fact]
    public async Task RolledBackNotificationTransactionLeavesNoDispatchJob()
    {
        await using var db=await Database(); var user=Guid.NewGuid(); db.USERS.Add(User(user)); await db.SaveChangesAsync();
        await using(var transaction=await db.Database.BeginTransactionAsync())
        {
            await new NotificationService(db,null!).CreateAsync(user,"TEST","Rollback");
            Assert.Equal(1,await db.AUDIT_LOGS.CountAsync(a=>a.action=="notification.dispatch.pending"));
            await transaction.RollbackAsync();
        }
        db.ChangeTracker.Clear(); Assert.Empty(await db.NOTIFICATIONS.ToListAsync()); Assert.Empty(await db.AUDIT_LOGS.ToListAsync());
    }

    private static async Task<DocShareDbContext> Database()
    {
        var db=new DocShareDbContext(new DbContextOptionsBuilder<DocShareDbContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync(); return db;
    }
    private static Users User(Guid id,string role="user")=>new(){user_id=id,Username=id.ToString(),Email=$"{id}@example.com",password_hash="test",Role=role};
    private static Documents Document(int id,Guid owner)=>new(){document_id=id,user_id=owner,Title=$"Doc {id}",public_id="shared-asset",asset_id="shared-asset",file_url="https://res.cloudinary.com/test/image/authenticated/v1/shared.pdf",thumbnail_url="",file_size=100,pages=1};
    private static T Auth<T>(T controller,Guid user,string role="admin")where T:ControllerBase{controller.ControllerContext=new(){HttpContext=new DefaultHttpContext()};controller.HttpContext.Items["DecodedToken"]=new DecodedTokenResponse{userID=user,roleID=role};return controller;}
    private static AdminController Admin(DocShareDbContext db,Guid user)=>Auth(new AdminController(db,null!,new NotificationService(db,null!)),user);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderMoveHonorsCompositeUniqueKeyAndRollsBackOnConstraintFailure(bool invalidTarget)
    {
        await using var db=await Database();var user=Guid.NewGuid();db.USERS.Add(User(user));db.DOCUMENTS.Add(Document(1,user));db.FOLDERS.AddRange(new Folders{folder_id=1,owner_user_id=user,name="First"},new Folders{folder_id=2,owner_user_id=user,name="Second"});db.FOLDER_DOCUMENTS.Add(new FolderDocuments{folder_id=1,document_id=1,added_by_user_id=user});await db.SaveChangesAsync();
        await using var transaction=await db.Database.BeginTransactionAsync();await DocumentFolderLinks.MoveAsync(db,1,invalidTarget?99:2,user);
        if(invalidTarget){await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());await transaction.RollbackAsync();}else{await db.SaveChangesAsync();await transaction.CommitAsync();}
        db.ChangeTracker.Clear();Assert.Equal(invalidTarget?1:2,await db.FOLDER_DOCUMENTS.Select(x=>x.folder_id).SingleAsync());
    }

    [Fact]
    public async Task UserListStorageAndSortIncludeDistinctRetainedVersions()
    {
        await using var db=await Database();var admin=Guid.NewGuid();var owner=Guid.NewGuid();db.USERS.AddRange(User(admin,"admin"),User(owner));db.DOCUMENTS.Add(Document(1,owner));db.DOCUMENT_VERSIONS.Add(new DocumentVersions{document_id=1,version_number=1,file_url="old",public_id="old",asset_id="old",file_size=50,uploaded_by=owner});await db.SaveChangesAsync();
        var response=Assert.IsType<OkObjectResult>(await Admin(db,admin).GetUsers(new PaginationParams(),null,null,null,"storage_used_bytes","desc"));var body=JsonSerializer.SerializeToElement(response.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(owner.ToString(),body.GetProperty("data")[0].GetProperty("user_id").GetString());Assert.Equal(150,body.GetProperty("data")[0].GetProperty("storage_used_bytes").GetInt64());
    }

    [Fact]
    public async Task HardDeleteQueuesCleanupAndPreservesAnAssetReferencedByCopy()
    {
        await using var db=await Database();var admin=Guid.NewGuid();db.USERS.Add(User(admin,"admin"));db.DOCUMENTS.AddRange(Document(1,admin),Document(2,admin));db.DOCUMENT_VERSIONS.Add(new DocumentVersions{document_id=1,version_number=1,file_url="old",public_id="old",asset_id="old",file_size=50,uploaded_by=admin});await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await Admin(db,admin).DeleteDocument(1));db.ChangeTracker.Clear();Assert.Equal(2,(await db.DOCUMENTS.SingleAsync()).document_id);Assert.Empty(await db.DOCUMENT_VERSIONS.ToListAsync());Assert.Equal(2,await db.AUDIT_LOGS.CountAsync(a=>a.action=="asset.cleanup.pending"));
    }

    [Fact]
    public async Task CategoryCyclesAndDeletionOfUsedTaxonomyAreRejected()
    {
        await using var db=await Database();var admin=Guid.NewGuid();db.USERS.Add(User(admin,"admin"));db.CATEGORIES.AddRange(new Categories{category_id="a",Name="A"},new Categories{category_id="b",Name="B",parent_id="a"});await db.SaveChangesAsync();
        var controller=Admin(db,admin);Assert.IsType<BadRequestObjectResult>(await controller.UpdateCategory("a",new(){ParentId="b"}));Assert.IsType<ConflictObjectResult>(await controller.DeleteCategory("a"));Assert.Null((await db.CATEGORIES.SingleAsync(c=>c.category_id=="a")).parent_id);
    }

    [Fact]
    public async Task RevokedAdminClaimUsesCurrentDatabaseRoleOnEveryRoute()
    {
        var name=Guid.NewGuid().ToString();var key=new string('x',64);var id=Guid.NewGuid();var jwt=new TokenServices(key).GenerateToken(id.ToString(),"admin");
        using var provider=new ServiceCollection().AddDbContext<DocShareDbContext>(o=>o.UseInMemoryDatabase(name)).BuildServiceProvider();
        using(var scope=provider.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<DocShareDbContext>();db.USERS.Add(User(id));db.TOKENS.Add(new Tokens{token_id=Guid.NewGuid(),user_id=id,token=TokenHasher.HashToken(jwt),type=TokenType.Access,is_active=true,expires_at=DateTime.UtcNow.AddDays(1)});await db.SaveChangesAsync();}
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["TokenSecretKey"]=key}).Build();string? observed=null;
        var middleware=new TokenValidationMiddleware(ctx=>{observed=(ctx.Items["DecodedToken"] as DecodedTokenResponse)?.roleID;return Task.CompletedTask;},config,provider.GetRequiredService<IServiceScopeFactory>());
        var request=new DefaultHttpContext();request.Request.Path="/api/library-items/1";request.Request.Headers.Authorization=$"Bearer {jwt}";await middleware.InvokeAsync(request);Assert.Equal("user",observed);
        request=new DefaultHttpContext();request.Response.Body=new MemoryStream();request.Request.Path="/api/admin/users";request.Request.Headers.Authorization=$"Bearer {jwt}";await middleware.InvokeAsync(request);Assert.Equal(403,request.Response.StatusCode);
    }

    [Fact]
    public async Task ContributorCannotUploadVersionOfAnotherOwnersDocument()
    {
        await using var db=await Database();var owner=Guid.NewGuid();var contributor=Guid.NewGuid();db.USERS.AddRange(User(owner),User(contributor));db.DOCUMENTS.Add(Document(1,owner));db.FOLDERS.Add(new Folders{folder_id=1,owner_user_id=owner,name="Private"});db.FOLDER_DOCUMENTS.Add(new FolderDocuments{folder_id=1,document_id=1,added_by_user_id=owner});db.FOLDER_MEMBERS.Add(new FolderMembers{folder_id=1,user_id=contributor,role="contributor"});await db.SaveChangesAsync();
        using var services=new ServiceCollection().AddHttpClient().BuildServiceProvider();var controller=Auth(new DocumentVersionsController(db,null!,new FolderPermissionService(db),new AuditLogService(db),services.GetRequiredService<IHttpClientFactory>(),new ConfigurationBuilder().Build()),contributor,"user");
        var bytes=System.Text.Encoding.ASCII.GetBytes("%PDF-1.7");var file=new FormFile(new MemoryStream(bytes),0,bytes.Length,"file","version.pdf"){Headers=new HeaderDictionary(),ContentType="application/pdf"};Assert.IsType<ForbidResult>(await controller.UploadNewVersion(1,file));Assert.Empty(await db.DOCUMENT_VERSIONS.ToListAsync());
    }
}
