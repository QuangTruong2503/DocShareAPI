using DocShareAPI.Data;
using DocShareAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Services;

public static class DocumentDependencyTracking
{
    // ExecuteDelete bypasses the change tracker. Detach those rows before removing
    // their principal so EF does not attempt to cascade-delete them a second time.
    public static void Detach(DocShareDbContext db, IReadOnlyCollection<int> ids)
    {
        foreach (var entry in db.ChangeTracker.Entries().ToArray())
        {
            var documentId = entry.Entity switch
            {
                FolderDocuments x => x.document_id, CollectionDocuments x => x.document_id,
                DocumentCategories x => x.document_id, DocumentTags x => x.document_id,
                Likes x => x.document_id, Reports x => x.document_id, Comments x => x.document_id,
                DocumentVersions x => x.document_id, DocumentViews x => x.document_id, DocumentDownloads x => x.document_id,
                ShareLinks x when x.item_type == "document" => x.item_id,
                Favorites x when x.item_type == "document" => x.item_id,
                _ => (int?)null
            };
            if (documentId.HasValue && ids.Contains(documentId.Value)) entry.State = EntityState.Detached;
        }
    }
}
