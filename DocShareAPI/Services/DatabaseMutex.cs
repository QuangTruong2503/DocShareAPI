using System.Data;
using DocShareAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Services;

public sealed class DatabaseMutex : IAsyncDisposable
{
    private readonly DocShareDbContext _db; private readonly string _key; private readonly bool _ownsConnection; private readonly bool _locked;
    private DatabaseMutex(DocShareDbContext db, string key, bool owns, bool locked) { _db = db; _key = key; _ownsConnection = owns; _locked = locked; }
    public static async Task<DatabaseMutex> Acquire(DocShareDbContext db, string key, int timeout = 10)
    {
        if (!db.Database.IsMySql()) return new(db,key,false,false);
        var owns = db.Database.GetDbConnection().State != ConnectionState.Open;
        if (owns) await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "SELECT GET_LOCK(@key, @timeout)";
            var parameter = command.CreateParameter(); parameter.ParameterName = "@key"; parameter.Value = key; command.Parameters.Add(parameter);
            var wait = command.CreateParameter(); wait.ParameterName = "@timeout"; wait.Value = timeout; command.Parameters.Add(wait);
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 1) throw new TimeoutException("Không lấy được khóa thao tác. Vui lòng thử lại.");
            return new(db,key,owns,true);
        }
        catch { if (owns) await db.Database.CloseConnectionAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        if (!_locked) return;
        try { await using var command = _db.Database.GetDbConnection().CreateCommand(); command.CommandText = "SELECT RELEASE_LOCK(@key)"; var parameter = command.CreateParameter(); parameter.ParameterName = "@key"; parameter.Value = _key; command.Parameters.Add(parameter); await command.ExecuteScalarAsync(); }
        finally { if (_ownsConnection) await _db.Database.CloseConnectionAsync(); }
    }
}
