using System.Text.Json;
using System.Text.Json.Nodes;
using DocShareAPI.Data;
using DocShareAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Services;

// Central boundary for legacy and workspace DTOs so neither previews nor versions leak CDN URLs.
public class AssetDeliveryFilter(DocShareDbContext db, AssetDelivery delivery) : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: not null } result)
        {
            var node = JsonSerializer.SerializeToNode(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var values = new List<(JsonObject Node, string Key, string Url)>();
            void Collect(JsonNode? value)
            {
                if (value is JsonObject obj)
                    foreach (var pair in obj.ToArray())
                    {
                        if (pair.Key is "file_url" or "fileUrl" or "previewUrl" or "thumbnail_url" or "thumbnailUrl" && pair.Value is JsonValue scalar && scalar.TryGetValue<string>(out var url) && url.StartsWith("https://res.cloudinary.com/", StringComparison.Ordinal)) values.Add((obj, pair.Key, url));
                        else Collect(pair.Value);
                    }
                else if (value is JsonArray array) foreach (var child in array) Collect(child);
            }
            Collect(node);
            if (values.Count > 0)
            {
                var urls = values.Select(x => x.Url).Distinct().ToList();
                var docs = await db.DOCUMENTS.AsNoTracking().Where(d => urls.Contains(d.file_url) || urls.Contains(d.thumbnail_url)).ToListAsync();
                var versions = await db.DOCUMENT_VERSIONS.AsNoTracking().Where(v => urls.Contains(v.file_url)).ToListAsync();
                var shareToken = context.HttpContext.Request.RouteValues.GetValueOrDefault("shareToken")?.ToString();
                var share = shareToken == null ? null : await db.SHARE_LINKS.AsNoTracking().FirstOrDefaultAsync(s => s.token == shareToken && s.revoked_at == null);
                foreach (var value in values)
                {
                    var thumb = value.Key is "thumbnail_url" or "thumbnailUrl";
                    int? id = null; foreach (var key in new[] { "document_id", "documentId" }) if (value.Node[key] is JsonValue number && number.TryGetValue<int>(out var parsed)) id = parsed;
                    if (id == null && value.Node["type"]?.ToString() == "document" && int.TryParse(value.Node["id"]?.ToString(), out var itemId)) id = itemId;
                    var document = docs.Where(d => (thumb ? d.thumbnail_url : d.file_url) == value.Url).OrderByDescending(d => d.document_id == id).ThenByDescending(d => d.user_id == (context.HttpContext.Items["DecodedToken"] as DecodedTokenResponse)?.userID).FirstOrDefault();
                    var version = thumb ? null : versions.FirstOrDefault(v => v.file_url == value.Url && (id == null || v.document_id == id));
                    var documentId = id ?? document?.document_id ?? version?.document_id;
                    value.Node[value.Key] = documentId.HasValue ? delivery.Issue(context.HttpContext, documentId.Value, document == null ? version?.version_id : null, thumb, share) : null;
                }
                result.Value = node;
            }
        }
        await next();
    }
}
