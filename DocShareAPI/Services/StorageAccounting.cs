using DocShareAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Services;

public static class StorageAccounting
{
    // Logical per-document storage: current files, trash, and each distinct retained
    // historical asset. Copying a document consumes storage even when its asset is shared.
    public static async Task<long> UsedAsync(DocShareDbContext db, Guid owner)
    {
        var documents = await db.DOCUMENTS.AsNoTracking().Where(d => d.user_id == owner).Select(d => new { d.document_id, d.asset_id, d.file_size }).ToListAsync();
        var ids = documents.Select(d => d.document_id).ToList();
        var versions = await db.DOCUMENT_VERSIONS.AsNoTracking().Where(v => ids.Contains(v.document_id)).Select(v => new { v.document_id, v.asset_id, v.file_size }).ToListAsync();
        var assets = documents.Concat(versions).GroupBy(x => new { x.document_id, x.asset_id });
        return assets.Sum(group => (long)group.Max(x => x.file_size)) + db.ChangeTracker.Entries<DocShareAPI.Models.Documents>().Where(e => e.State == EntityState.Added && e.Entity.user_id == owner).Sum(e => (long)e.Entity.file_size);
    }
}
