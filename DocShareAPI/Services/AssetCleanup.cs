using System.Text.Json;
using CloudinaryDotNet.Actions;
using DocShareAPI.Data;
using DocShareAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Services;

public record CleanupAsset(string PublicId, string AssetId, string DeliveryType);
public static class AssetCleanup
{
    public static void Queue(DocShareDbContext db, string publicId, string assetId, string fileUrl)
    {
        if (string.IsNullOrWhiteSpace(publicId)) return;
        db.AUDIT_LOGS.Add(new AuditLogs { action = "asset.cleanup.pending", entity_type = "asset", entity_id = assetId, metadata = JsonSerializer.Serialize(new CleanupAsset(publicId, assetId, fileUrl.Contains("/authenticated/") ? "authenticated" : "upload")) });
    }
    public static async Task QueueDocuments(DocShareDbContext db, IEnumerable<int> ids)
    {
        var list = ids.ToList();
        foreach (var doc in await db.DOCUMENTS.AsNoTracking().Where(d => list.Contains(d.document_id)).ToListAsync()) Queue(db, doc.public_id, doc.asset_id, doc.file_url);
        foreach (var version in await db.DOCUMENT_VERSIONS.AsNoTracking().Where(v => list.Contains(v.document_id)).ToListAsync()) Queue(db, version.public_id, version.asset_id, version.file_url);
    }
}
public class AssetCleanupWorker(IServiceScopeFactory scopes, ILogger<AssetCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DocShareDbContext>();
                var cloud = scope.ServiceProvider.GetRequiredService<ICloudinaryService>();
                var cutoff = DateTime.UtcNow.AddMinutes(-1);
                var jobs = await db.AUDIT_LOGS.Where(a => a.action == "asset.cleanup.pending" && a.created_at < cutoff).OrderBy(a => a.audit_id).Take(25).ToListAsync(stoppingToken);
                foreach (var job in jobs)
                {
                    var asset = JsonSerializer.Deserialize<CleanupAsset>(job.metadata ?? ""); if (asset == null) continue;
                    var referenced = await db.DOCUMENTS.AnyAsync(d => d.asset_id == asset.AssetId || d.public_id == asset.PublicId, stoppingToken) || await db.DOCUMENT_VERSIONS.AnyAsync(v => v.asset_id == asset.AssetId || v.public_id == asset.PublicId, stoppingToken);
                    if (referenced) { job.action = "asset.cleanup.retained"; await db.SaveChangesAsync(stoppingToken); continue; }
                    var result = await cloud.Cloudinary.DestroyAsync(new DeletionParams(asset.PublicId) { Type = asset.DeliveryType, ResourceType = ResourceType.Image, Invalidate = true });
                    if (result.Error != null || result.Result is not ("ok" or "not found")) continue;
                    job.action = "asset.cleanup.completed"; await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "Asset cleanup failed; queued jobs will be retried."); }
        }
    }
}
