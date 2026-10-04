using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocShareAPI.Data;
using DocShareAPI.Helpers;
using Microsoft.EntityFrameworkCore;

namespace DocShareAPI.Services;

// Challenge state is persisted with the temporary token, using its existing device
// metadata field. Existing databases must widen TOKENS.user_device to TEXT;
// see docs/two-factor-challenge-migration.sql.
public static class TwoFactorChallenges
{
    private record State(string CodeHash, int Attempts, int Resends, DateTime SentAt, string? GoogleSubject = null);
    public static string? PendingGoogleSubject(string? state) => Read(state)?.GoogleSubject;
    private static State? Read(string? value) { try { return JsonSerializer.Deserialize<State>(value ?? ""); } catch (JsonException) { return null; } }
    private static string Hash(Guid id, string code) => TokenHasher.HashToken($"{id:N}:{code}");
    public static async Task<bool> SaveAsync(DocShareDbContext db, Guid challenge, string code, bool resend = false, string? googleSubject = null)
    {
        await using var mutex = await DatabaseMutex.Acquire(db, $"docshare:2fa:{challenge:N}");
        var token = await db.TOKENS.FindAsync(challenge); if (token == null) return false;
        await db.Entry(token).ReloadAsync(); if (!token.is_active || token.expires_at <= DateTime.UtcNow) return false;
        var old = Read(token.user_device);
        if (resend && (old == null || old.Resends >= 3 || old.Attempts >= 5 || DateTime.UtcNow - old.SentAt < TimeSpan.FromSeconds(60))) return false;
        token.user_device = JsonSerializer.Serialize(new State(Hash(challenge,code), old?.Attempts ?? 0, (old?.Resends ?? 0) + (resend ? 1 : 0), DateTime.UtcNow, googleSubject ?? old?.GoogleSubject));
        await db.SaveChangesAsync(); return true;
    }
    public static async Task<bool> VerifyAsync(DocShareDbContext db, Guid challenge, string code)
    {
        await using var mutex = await DatabaseMutex.Acquire(db, $"docshare:2fa:{challenge:N}");
        var token = await db.TOKENS.FindAsync(challenge); if (token == null) return false;
        await db.Entry(token).ReloadAsync(); if (!token.is_active || token.expires_at <= DateTime.UtcNow) return false;
        var state = Read(token.user_device); if (state == null) return false;
        if (state.Attempts >= 5) { token.is_active = false; await db.SaveChangesAsync(); return false; }
        var valid = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state.CodeHash), Encoding.UTF8.GetBytes(Hash(challenge,code)));
        token.user_device = JsonSerializer.Serialize(state with { Attempts = state.Attempts + 1 });
        if (valid || state.Attempts + 1 >= 5) token.is_active = false;
        await db.SaveChangesAsync(); return valid;
    }
}
