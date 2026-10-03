using System.Data;
using DocShareAPI.Data;
using DocShareAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Middleware;

// MySQL advisory locks serialize quota admission/version numbering across instances.
// They use the request connection and are always released, including aborted requests.
public class MutationLockMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, DocShareDbContext db)
    {
        var path = context.Request.Path.Value ?? "";
        string? key = null;
        if (context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE")
        {
            if (path.StartsWith("/api/admin/users", StringComparison.OrdinalIgnoreCase)) key = "docshare:admin-users";
            else if (context.Items["DecodedToken"] is DecodedTokenResponse user && (path.StartsWith("/api/documents", StringComparison.OrdinalIgnoreCase) || path == "/api/library-items/copy"))
            {
                var owner = user.userID;
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length > 2 && int.TryParse(segments[2], out var id)) owner = await db.DOCUMENTS.Where(d => d.document_id == id).Select(d => (Guid?)d.user_id).FirstOrDefaultAsync() ?? owner;
                key = $"docshare:storage:{owner:N}";
            }
        }
        if (key == null || !db.Database.IsMySql()) { await next(context); return; }
        await db.Database.OpenConnectionAsync(context.RequestAborted);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT GET_LOCK(@key, 10)";
            var parameter = command.CreateParameter(); parameter.ParameterName = "@key"; parameter.Value = key; command.Parameters.Add(parameter);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(context.RequestAborted)) != 1) { context.Response.StatusCode = 429; await context.Response.WriteAsJsonAsync(new { message = "Đang có thao tác khác trên cùng tài khoản. Vui lòng thử lại." }); return; }
            try
            {
                if (key == "docshare:admin-users" && context.Items["DecodedToken"] is DecodedTokenResponse actor)
                {
                    var role = await db.USERS.AsNoTracking().Where(u => u.user_id == actor.userID).Select(u => u.Role).FirstOrDefaultAsync();
                    if (role != "admin") { context.Response.StatusCode = 403; return; }
                    actor.roleID = role;
                }
                await next(context);
            }
            finally { command.CommandText = "SELECT RELEASE_LOCK(@key)"; await command.ExecuteScalarAsync(CancellationToken.None); }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
