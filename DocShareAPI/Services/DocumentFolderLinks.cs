using DocShareAPI.Data;
using DocShareAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Services;

public static class DocumentFolderLinks
{
    // folder_id is part of the primary key. Delete before inserting the replacement
    // so the unique document_id constraint is also respected inside the caller's transaction.
    public static async Task MoveAsync(DocShareDbContext db, int documentId, int? folderId, Guid actor)
    {
        var current = await db.FOLDER_DOCUMENTS.FirstOrDefaultAsync(x => x.document_id == documentId);
        if (current?.folder_id == folderId) return;
        if (current != null)
        {
            db.FOLDER_DOCUMENTS.Remove(current);
            await db.SaveChangesAsync();
        }
        if (folderId.HasValue)
            db.FOLDER_DOCUMENTS.Add(new FolderDocuments { document_id = documentId, folder_id = folderId.Value, added_by_user_id = actor, added_at = DateTime.UtcNow });
    }
}
