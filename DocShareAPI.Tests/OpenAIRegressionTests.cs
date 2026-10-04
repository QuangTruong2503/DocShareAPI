using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CloudinaryDotNet;
using DocShareAPI.Controllers.Public;
using DocShareAPI.Data;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DocShareAPI.Tests;

public class OpenAIRegressionTests
{
    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return send(request, token); }
    }
    internal sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    internal sealed class ChunkedContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
    internal sealed class Cloud : ICloudinaryService
    {
        public string CloudName => "fixture";
        public Cloudinary Cloudinary { get; } = new(new Account("fixture", "fixture-key", "fixture-secret"));
    }
    internal sealed class FakeAI : IOpenAITextService
    {
        public int Calls; public string? Input;
        public Task<string> GenerateAsync(string instructions, string input, CancellationToken token) { Calls++; Input = input; return Task.FromResult("Tóm tắt mẫu"); }
    }
    internal sealed class Reader : IAIDocumentReader
    {
        public int Calls;
        public Task<AIExtractedDocument> ReadAsync(string url, CancellationToken token) { Calls++; return Task.FromResult(new AIExtractedDocument("Text fixture", true, 6, 2)); }
    }
    internal static byte[] Pdf(int pages = 1, bool blank = false)
    {
        var builder = new PdfDocumentBuilder(); var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 1; i <= pages; i++) { var page = builder.AddPage(PageSize.A4); if (!blank) page.AddText($"Page {i}: A library trial included 120 students. Quiz scores improved by 30 percent.", 12, new PdfPoint(30, 700), font); }
        return builder.Build();
    }
    internal static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    internal static string Completed(string text) => JsonSerializer.Serialize(new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text } } } } });

    [Fact]
    public void ProductionEnvironmentKeyTakesPrecedenceAndBlankEnvironmentFallsBackToAppsettings()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["OpenAIKey"] = " local-key ", ["OpenAI:Model"] = "custom-model" }).Build();
        Assert.Equal("production-key", OpenAIOptions.FromConfiguration(config, name => name == "OPEN_AI_API_KEY" ? "production-key" : null).ApiKey);
        Assert.Equal("local-key", OpenAIOptions.FromConfiguration(config, _ => " ").ApiKey);
        Assert.Equal("custom-model", OpenAIOptions.FromConfiguration(config, _ => null).Model);
    }

    [Fact]
    public async Task ResponsesRequestKeepsKeyOutOfBodyAndReadsAllMessageTextAfterReasoning()
    {
        var handler = new Handler(async (request, token) =>
        {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.ToString()); Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Assert.Equal("fixture-key", request.Headers.Authorization.Parameter);
            var body = await request.Content!.ReadAsStringAsync(token); Assert.DoesNotContain("fixture-key", body); var json = JsonSerializer.Deserialize<JsonElement>(body); Assert.False(json.GetProperty("store").GetBoolean()); Assert.Equal("instructions", json.GetProperty("instructions").GetString()); Assert.Equal("Xin chào", json.GetProperty("input").GetString());
            return Response("{\"status\":\"completed\",\"output\":[{\"type\":\"reasoning\"},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"Đoạn một\"},{\"type\":\"output_text\",\"text\":\"Đoạn hai\"}]}]}");
        }); using var http = new HttpClient(handler); var service = new OpenAITextService(http, new() { ApiKey = "fixture-key" });
        Assert.Equal("Đoạn một\nĐoạn hai", await service.GenerateAsync("instructions", "Xin chào", default));
    }

    [Theory]
    [InlineData(401, 503, "AI_CONFIGURATION_ERROR", 1)]
    [InlineData(403, 503, "AI_CONFIGURATION_ERROR", 1)]
    [InlineData(400, 502, "AI_UPSTREAM_ERROR", 1)]
    [InlineData(429, 429, "AI_RATE_LIMITED", 3)]
    [InlineData(500, 502, "AI_UPSTREAM_ERROR", 3)]
    public async Task ProviderFailuresAreMappedWithoutLeakingKeyOrUpstreamBody(int upstream, int expected, string code, int calls)
    {
        var handler = new Handler((_, _) => Task.FromResult(Response("secret-provider-body", (HttpStatusCode)upstream))); using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<AIServiceException>(() => new OpenAITextService(http, new() { ApiKey = "secret-key" }).GenerateAsync("", "input", default));
        Assert.Equal(expected, error.StatusCode); Assert.Equal(code, error.Code); Assert.DoesNotContain("secret", error.Message); Assert.Equal(calls, handler.Calls);
    }

    [Theory]
    [InlineData("not-json", "AI_INVALID_RESPONSE")]
    [InlineData("{\"status\":\"incomplete\",\"output\":[]}", "AI_INCOMPLETE_RESPONSE")]
    [InlineData("{\"status\":\"completed\",\"output\":[]}", "AI_EMPTY_RESPONSE")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\"}]}]}", "AI_REFUSED")]
    public async Task InvalidIncompleteEmptyAndRefusedResultsCannotBePresentedAsSuccess(string response, string code)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Response(response)))); var error = await Assert.ThrowsAsync<AIServiceException>(() => new OpenAITextService(http, new() { ApiKey = "fixture" }).GenerateAsync("", "input", default)); Assert.Equal(code, error.Code);
    }

    [Fact]
    public async Task TransientFailureIsRetriedAndMissingKeyMakesNoRequest()
    {
        var count = 0; var handler = new Handler((_, _) => Task.FromResult(++count == 1 ? Response("", HttpStatusCode.ServiceUnavailable) : Response(Completed("Recovered")))); using var http = new HttpClient(handler);
        var missing = await Assert.ThrowsAsync<AIServiceException>(() => new OpenAITextService(http, new()).GenerateAsync("", "input", default)); Assert.Equal(503, missing.StatusCode); Assert.Equal(0, handler.Calls);
        Assert.Equal("Recovered", await new OpenAITextService(http, new() { ApiKey = "fixture" }).GenerateAsync("", "input", default)); Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task DeadlineAndCallerCancellationAreDistinguished()
    {
        using var http = new HttpClient(new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response(""); })); var service = new OpenAITextService(http, new() { ApiKey = "fixture", TimeoutSeconds = 1 });
        var timeout = await Assert.ThrowsAsync<AIServiceException>(() => service.GenerateAsync("", "input", default)); Assert.Equal(504, timeout.StatusCode);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GenerateAsync("", "input", cancelled.Token));
    }

    [Fact]
    public async Task PdfExtractionWorksPastFourPagesAndReportsBoundedPartialText()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Pdf(6)) })));
        var reader = new AIDocumentReader(new Factory(http), new Cloud(), new()); var text = await reader.ReadAsync("https://res.cloudinary.com/fixture/image/upload/test.pdf", default);
        Assert.Contains("Page 6", text.Text); Assert.False(text.Truncated); Assert.Equal(6, text.ProcessedPages);
        var partial = await new AIDocumentReader(new Factory(http), new Cloud(), new() { MaxDocumentPages = 2 }).ReadAsync("https://res.cloudinary.com/fixture/image/upload/test.pdf", default); Assert.True(partial.Truncated); Assert.Equal(2, partial.ProcessedPages); Assert.DoesNotContain("Page 3", partial.Text);
    }

    [Theory]
    [InlineData("https://example.com/fixture/image/upload/test.pdf")]
    [InlineData("http://res.cloudinary.com/fixture/image/upload/test.pdf")]
    [InlineData("https://res.cloudinary.com/other/image/upload/test.pdf")]
    public async Task DocumentUrlValidationBlocksExternalOrUntrustedSources(string url)
    {
        var handler = new Handler((_, _) => throw new Exception("Must not fetch")); using var http = new HttpClient(handler); var error = await Assert.ThrowsAsync<AIServiceException>(() => new AIDocumentReader(new Factory(http), new Cloud(), new()).ReadAsync(url, default)); Assert.Equal(422, error.StatusCode); Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PdfSizeLimitAppliesToDeclaredAndStreamedBytes(bool declared)
    {
        HttpContent content = declared ? new ByteArrayContent(Pdf()) : new ChunkedContent(Pdf());
        Assert.Equal(declared, content.Headers.ContentLength.HasValue);
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content })));
        var error = await Assert.ThrowsAsync<AIServiceException>(() => new AIDocumentReader(new Factory(http), new Cloud(), new() { MaxPdfBytes = 20 }).ReadAsync("https://res.cloudinary.com/fixture/image/upload/test.pdf", default));
        Assert.Equal(413, error.StatusCode);
        Assert.Equal("AI_DOCUMENT_TOO_LARGE", error.Code);
    }

    [Fact]
    public async Task AuthenticatedCloudinaryPdfIsSignedOnTheServerBeforeExtraction()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("res.cloudinary.com", request.RequestUri!.Host);
            Assert.Contains("/fixture/image/authenticated/s--", request.RequestUri.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Pdf()) });
        }));
        var extracted = await new AIDocumentReader(new Factory(http), new Cloud(), new()).ReadAsync("https://res.cloudinary.com/fixture/image/authenticated/v123/test.pdf", default);
        Assert.Contains("120 students", extracted.Text);
    }

    [Theory]
    [InlineData(false, "AI_INVALID_PDF")]
    [InlineData(true, "AI_PDF_NO_TEXT")]
    public async Task InvalidAndImageOnlyPdfGiveActionableErrors(bool blank, string code)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(blank ? Pdf(blank: true) : Encoding.UTF8.GetBytes("not a pdf")) })));
        var error = await Assert.ThrowsAsync<AIServiceException>(() => new AIDocumentReader(new Factory(http), new Cloud(), new()).ReadAsync("https://res.cloudinary.com/fixture/image/upload/test.pdf", default)); Assert.Equal(code, error.Code);
    }

    [Fact]
    public async Task PrivateAndTrashDocumentsAreRejectedBeforeExtractionOrOpenAI()
    {
        await using var db = new DocShareDbContext(new DbContextOptionsBuilder<DocShareDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options); var owner = Guid.NewGuid(); var viewer = Guid.NewGuid();
        db.DOCUMENTS.Add(new Documents { document_id = 1, user_id = owner, Title = "Private", public_id = "fixture", asset_id = "fixture", file_url = "fixture", thumbnail_url = "", pages = 1 }); await db.SaveChangesAsync(); var ai = new FakeAI(); var reader = new Reader();
        var controller = new AIGenerateController(db, ai, reader, new FolderPermissionService(db)) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<UnauthorizedObjectResult>(await controller.SummarizeDocument(1, default)); controller.HttpContext.Items["DecodedToken"] = new DecodedTokenResponse { userID = viewer, roleID = "user" }; Assert.IsType<ForbidResult>(await controller.SummarizeDocument(1, default));
        controller.HttpContext.Items["DecodedToken"] = new DecodedTokenResponse { userID = owner, roleID = "user" }; Assert.IsType<OkObjectResult>(await controller.SummarizeDocument(1, default)); Assert.Equal(1, ai.Calls);
        db.DOCUMENTS.Single().deleted_at = DateTime.UtcNow; await db.SaveChangesAsync(); Assert.IsType<NotFoundObjectResult>(await controller.SummarizeDocument(1, default)); Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task CanonicalAndLegacyRoutesAreCompiledAndAcceptCamelCaseClientContract()
    {
        var ai = new FakeAI(); var databaseName = Guid.NewGuid().ToString();
        using var server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(AIGenerateController).Assembly); services.AddDbContext<DocShareDbContext>(options => options.UseInMemoryDatabase(databaseName)); services.AddSingleton<IOpenAITextService>(ai); services.AddSingleton<IAIDocumentReader>(new Reader()); services.AddScoped<IFolderPermissionService, FolderPermissionService>();
        }).Configure(app => { app.UseRouting(); app.UseEndpoints(endpoints => endpoints.MapControllers()); })); using var client = server.CreateClient();
        foreach (var route in new[] { "/api/public/ai/chat", "/api/public/gemini/chat" }) { using var result = await client.PostAsJsonAsync(route, new { message = "Xin chào" }); Assert.Equal(HttpStatusCode.OK, result.StatusCode); Assert.Equal("Tóm tắt mẫu", (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()); }
        using var empty = await client.PostAsJsonAsync("/api/public/ai/chat", new { message = "  " }); Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using var longRequest = await client.PostAsJsonAsync("/api/public/ai/chat", new { message = new string('x', 20001) }); Assert.Equal(HttpStatusCode.BadRequest, longRequest.StatusCode); Assert.Equal(2, ai.Calls);
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocShareDbContext>();
            db.DOCUMENTS.Add(new Documents { document_id = 1, user_id = Guid.NewGuid(), Title = "Public PDF", is_public = true, public_id = "fixture", asset_id = "fixture", file_url = "fixture", thumbnail_url = "", pages = 6 });
            await db.SaveChangesAsync();
        }
        foreach (var route in new[] { "/api/public/ai/document-summary", "/api/public/gemini/document-summary" })
        {
            using var result = await client.GetAsync($"{route}?documentId=1");
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var json = await result.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Tóm tắt mẫu", json.GetProperty("summary").GetString());
            Assert.True(json.GetProperty("truncated").GetBoolean());
            Assert.Equal(6, json.GetProperty("total_pages").GetInt32());
            Assert.Equal(2, json.GetProperty("processed_pages").GetInt32());
        }
        Assert.Equal(4, ai.Calls);
    }
}

public sealed class OpenAILiveFactAttribute : FactAttribute
{
    public OpenAILiveFactAttribute() { if (Environment.GetEnvironmentVariable("DOCSHARE_OPENAI_LIVE_TEST") != "1") Skip = "Set DOCSHARE_OPENAI_LIVE_TEST=1 to use the configured OpenAI key with synthetic data."; }
}

public class OpenAILiveTests
{
    [OpenAILiveFact]
    [Trait("Category", "OpenAILive")]
    public async Task RealOpenAIChatAndSyntheticPdfSummaryReturnUsefulVietnameseText()
    {
        var configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../DocShareAPI/appsettings.json")); var config = new ConfigurationBuilder().AddJsonFile(configPath).Build(); var options = OpenAIOptions.FromConfiguration(config); Assert.False(string.IsNullOrWhiteSpace(options.ApiKey)); using var http = new HttpClient(); var ai = new OpenAITextService(http, options);
        var chat = await ai.GenerateAsync("Trả lời ngắn bằng tiếng Việt.", "2 + 2 bằng bao nhiêu?", default); Assert.Contains("4", chat);
        using var documents = new HttpClient(new OpenAIRegressionTests.Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(OpenAIRegressionTests.Pdf(6)) })));
        var extracted = await new AIDocumentReader(new OpenAIRegressionTests.Factory(documents), new OpenAIRegressionTests.Cloud(), options).ReadAsync("https://res.cloudinary.com/fixture/image/upload/test.pdf", default); Assert.Equal(6, extracted.ProcessedPages);
        var summary = await ai.GenerateAsync("Tóm tắt bằng tiếng Việt, giữ nguyên số người tham gia và phần trăm cải thiện; không bịa thông tin.", extracted.Text, default); Assert.Contains("120", summary); Assert.Contains("30", summary); Assert.True(summary.Length > 30);
    }
}
