using System.Text.Json;
using DocShareAPI.Controllers;
using DocShareAPI.Data;
using DocShareAPI.Models;
using DocShareAPI.Services;
using DocShareAPI.Services.EmailServices;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocShareAPI.Tests;

public class GoogleEmailLoginTests
{
    private sealed class Email : ITwoFactorEmailService
    {
        public int Calls;
        public Task SendTwoFactorCodeAsync(string email, string name, string code, string request)
        { Calls++; return Task.CompletedTask; }
    }

    private sealed class Controller(DocShareDbContext db, Email email, bool verified)
        : UsersController(db, null!, NullLogger<UsersController>.Instance,
            new TokenServices(new string('x', 64)),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Google:ClientId"] = "fixture" }).Build(), null!, email)
    {
        protected override Task<GoogleJsonWebSignature.Payload> ValidateGoogleTokenAsync(string token, string clientId)
            => Task.FromResult(new GoogleJsonWebSignature.Payload
            {
                Email = "existing@example.com", EmailVerified = verified, Subject = "google-subject",
                Issuer = "https://accounts.google.com",
                ExpirationTimeSeconds = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()
            });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task VerifiedGoogleEmailReusesExistingAccountWithoutPassword(bool localVerified, bool twoFactor)
    {
        await using var db = new DocShareDbContext(new DbContextOptionsBuilder<DocShareDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync();
        var id = Guid.NewGuid();
        db.USERS.Add(new Users { user_id = id, Username = "existing", Email = "Existing@Example.com",
            password_hash = "unused-password-hash", is_verified = localVerified,
            two_factor_enabled = twoFactor, two_factor_method = TwoFactorMethod.Email });
        await db.SaveChangesAsync();
        var email = new Email();
        var controller = new Controller(db, email, true)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        controller.HttpContext.Request.Path = "/api/Users/public/request-login-google";

        var result = Assert.IsType<OkObjectResult>(await controller.GoogleLogin(new() { token = "fixture", userDevice = "device" }));
        var body = JsonSerializer.SerializeToElement(result.Value);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Single(await db.USERS.ToListAsync());
        Assert.True((await db.USERS.SingleAsync()).is_verified);
        Assert.Equal("unused-password-hash", (await db.USERS.SingleAsync()).password_hash);
        if (twoFactor)
        {
            Assert.True(body.GetProperty("require2FA").GetBoolean());
            Assert.Empty(await db.EXTERNAL_IDENTITIES.ToListAsync());
            Assert.Empty(await db.TOKENS.Where(t => t.type == TokenType.Access).ToListAsync());
            Assert.Equal(1, email.Calls);
        }
        else
        {
            Assert.True(body.GetProperty("isLogin").GetBoolean());
            Assert.Equal(id, (await db.EXTERNAL_IDENTITIES.SingleAsync()).user_id);
            Assert.Single(await db.TOKENS.Where(t => t.type == TokenType.Access).ToListAsync());
        }
    }

    [Fact]
    public async Task UnverifiedGoogleEmailCannotLogIn()
    {
        await using var db = new DocShareDbContext(new DbContextOptionsBuilder<DocShareDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var controller = new Controller(db, new Email(), false);
        Assert.IsType<UnauthorizedObjectResult>(await controller.GoogleLogin(new() { token = "fixture" }));
    }
}
