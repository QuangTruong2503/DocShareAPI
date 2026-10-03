using DocShareAPI.Data;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Controllers;

[ApiController]
public class AssetContentController(DocShareDbContext db, AssetDelivery delivery, ICloudinaryService cloud, IHttpClientFactory clients) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("/api/public/assets/archive")]
    public async Task<IActionResult> Archive([FromQuery] string grant, CancellationToken cancellation)
    {
        var ticket = delivery.Read(grant);
        if (ticket?.ArchiveIds == null || ticket.ArchiveIds.Length is 0 or > 100) return Unauthorized();
        using var output = new MemoryStream();
        long total = 0;
        using (var zip = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            foreach (var id in ticket.ArchiveIds.Distinct())
            {
                var doc = await db.DOCUMENTS.AsNoTracking().FirstOrDefaultAsync(d => d.document_id == id, cancellation);
                if (doc == null || !await delivery.CanRead(doc, ticket with { DocumentId = id })) return NotFound();
                if (!Uri.TryCreate(doc.file_url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "res.cloudinary.com" || !uri.AbsolutePath.StartsWith($"/{cloud.CloudName}/", StringComparison.Ordinal)) return NotFound();
                using var response = await clients.CreateClient().GetAsync(AssetDelivery.OriginUrl(cloud, doc.file_url), HttpCompletionOption.ResponseHeadersRead, cancellation);
                if (!response.IsSuccessStatusCode) return StatusCode(502);
                var name = string.Concat(doc.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '_' : c));
                var entry = zip.CreateEntry($"{id}-{name}");
                await using var target = entry.Open(); await using var input = await response.Content.ReadAsStreamAsync(cancellation);
                var buffer = new byte[65536]; int length;
                while ((length = await input.ReadAsync(buffer, cancellation)) > 0)
                {
                    total += length; if (total > 64L * 1024 * 1024) return StatusCode(413, new { message = "Tổng dung lượng mỗi lần tải tối đa 64 MB. Hãy chia nhỏ danh sách tài liệu." });
                    await target.WriteAsync(buffer.AsMemory(0,length), cancellation);
                }
            }
        }
        Response.Headers.CacheControl = "private, no-store";
        return File(output.ToArray(), "application/zip", "docshare-documents.zip");
    }
    [AllowAnonymous]
    [HttpGet("/api/public/assets/{documentId:int}")]
    public async Task<IActionResult> Get(int documentId, [FromQuery] string grant, CancellationToken cancellation)
    {
        var ticket = delivery.Read(grant);
        if (ticket == null || ticket.DocumentId != documentId) return Unauthorized(new { message = "Quyền truy cập nội dung đã hết hạn. Vui lòng tải lại tài liệu." });
        var document = await db.DOCUMENTS.AsNoTracking().FirstOrDefaultAsync(d => d.document_id == documentId, cancellation);
        if (document == null || !await delivery.CanRead(document, ticket)) return NotFound();
        var source = ticket.Thumbnail ? document.thumbnail_url : document.file_url;
        if (ticket.VersionId.HasValue)
            source = await db.DOCUMENT_VERSIONS.Where(v => v.document_id == documentId && v.version_id == ticket.VersionId).Select(v => v.file_url).FirstOrDefaultAsync(cancellation);
        if (source == null || !Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "res.cloudinary.com" || !uri.AbsolutePath.StartsWith($"/{cloud.CloudName}/", StringComparison.Ordinal)) return NotFound();
        using var response = await clients.CreateClient().GetAsync(AssetDelivery.OriginUrl(cloud, source), HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode) return StatusCode(502, new { message = "Không tải được nội dung tài liệu." });
        // Uploads are capped; cap legacy assets too before buffering for range support.
        const int max = 64 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > max) return StatusCode(413);
        await using var input = await response.Content.ReadAsStreamAsync(cancellation);
        using var output = new MemoryStream(); var buffer = new byte[65536]; int length;
        while ((length = await input.ReadAsync(buffer, cancellation)) > 0) { if (output.Length + length > max) return StatusCode(413); await output.WriteAsync(buffer.AsMemory(0,length), cancellation); }
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(output.ToArray(), response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", enableRangeProcessing: true);
    }
}
