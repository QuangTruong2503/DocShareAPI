using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocShareAPI.Data;
using DocShareAPI.Helpers;
using DocShareAPI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;

namespace DocShareAPI.Services;

public record AssetGrant(int DocumentId, int? VersionId, bool Thumbnail, Guid? UserId, string? SessionHash, string? ShareToken, string? ShareRevision, long Expires, int[]? ArchiveIds = null);

public class AssetDelivery
{
    private readonly DocShareDbContext _db;
    private readonly IFolderPermissionService _permissions;
    private readonly byte[] _key;
    private readonly IConfiguration _config;
    public AssetDelivery(DocShareDbContext db, IFolderPermissionService permissions, IConfiguration config)
    {
        _db = db; _permissions = permissions; _config = config;
        _key = Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("JWT_SECRET_KEY") ?? config["TokenSecretKey"] ?? throw new InvalidOperationException("JWT secret is required."));
    }
    public static string ShareRevision(ShareLinks link) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{link.access}|{link.permission}|{link.password_hash}|{link.allow_download}|{link.expires_at:O}|{link.revoked_at:O}")));
    public string Issue(HttpContext context, int documentId, int? versionId = null, bool thumbnail = false, ShareLinks? share = null, int[]? archiveIds = null)
    {
        var user = context.Items["DecodedToken"] as DecodedTokenResponse;
        var bearer = context.Request.Headers.Authorization.ToString();
        var session = bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? TokenHasher.HashToken(bearer[7..].Trim()) : null;
        var grant = new AssetGrant(documentId, versionId, thumbnail, share == null ? user?.userID : null, session, share?.token, share == null ? null : ShareRevision(share), DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(), archiveIds);
        var payload = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(grant));
        var signature = WebEncoders.Base64UrlEncode(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload)));
        var origin = (Environment.GetEnvironmentVariable("API_BASE_URL") ?? _config["ApiBaseUrl"] ?? $"{context.Request.Scheme}://{context.Request.Host}").TrimEnd('/');
        return $"{origin}/api/public/assets/{(archiveIds == null ? documentId.ToString() : "archive")}?grant={payload}.{signature}";
    }
    public AssetGrant? Read(string value)
    {
        try
        {
            var parts = value.Split('.'); if (parts.Length != 2) return null;
            var expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(parts[0]));
            if (!CryptographicOperations.FixedTimeEquals(expected, WebEncoders.Base64UrlDecode(parts[1]))) return null;
            var grant = JsonSerializer.Deserialize<AssetGrant>(WebEncoders.Base64UrlDecode(parts[0]));
            return grant?.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds() ? grant : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException) { return null; }
    }
    public async Task<bool> CanRead(Documents document, AssetGrant grant)
    {
        if (document.deleted_at != null || document.document_id != grant.DocumentId) return false;
        if (grant.ShareToken != null)
        {
            var share = await _db.SHARE_LINKS.AsNoTracking().FirstOrDefaultAsync(s => s.token == grant.ShareToken && s.revoked_at == null);
            if (share == null || ShareRevision(share) != grant.ShareRevision || share.expires_at <= DateTime.UtcNow || share.access != "anyone_with_link") return false;
            if (share.item_type == "document") return share.item_id == document.document_id;
            return share.item_type == "folder" && await _db.FOLDERS.AnyAsync(f => f.folder_id == share.item_id && f.deleted_at == null) && await _db.FOLDER_DOCUMENTS.AnyAsync(fd => fd.document_id == document.document_id && fd.folder_id == share.item_id);
        }
        if (grant.UserId.HasValue)
        {
            if (grant.SessionHash == null || !await _db.TOKENS.AnyAsync(t => t.user_id == grant.UserId && t.type == TokenType.Access && t.token == grant.SessionHash && t.is_active && t.expires_at > DateTime.UtcNow)) return false;
            var role = await _db.USERS.Where(u => u.user_id == grant.UserId).Select(u => u.Role).FirstOrDefaultAsync();
            if (role == null) return false;
            if (document.user_id == grant.UserId || role == "admin") return true;
            var folder = await _db.FOLDER_DOCUMENTS.Where(fd => fd.document_id == document.document_id).Select(fd => (int?)fd.folder_id).FirstOrDefaultAsync();
            if (folder.HasValue && await _permissions.CanViewFolderAsync(grant.UserId, folder.Value)) return true;
        }
        return document.is_public;
    }
    // Signed CDN URLs are used only by the server; clients receive a short-lived gateway grant.
    public static string OriginUrl(ICloudinaryService cloud, string source)
    {
        var prefix = $"https://res.cloudinary.com/{cloud.CloudName}/image/authenticated/";
        if (!source.StartsWith(prefix, StringComparison.Ordinal)) return source;
        var path = source[prefix.Length..];
        var match = System.Text.RegularExpressions.Regex.Match(path, @"^(?:(?<transform>.*)/)?v(?<version>\d+)/(?<source>.+)$");
        var builder = cloud.Cloudinary.Api.UrlImgUp.Secure(true).Type("authenticated").Signed(true).ForceVersion(false);
        if (!match.Success) return builder.BuildUrl(path);
        builder.Version(match.Groups["version"].Value);
        if (match.Groups["transform"].Success) builder.Transform(new CloudinaryDotNet.Transformation().RawTransformation(match.Groups["transform"].Value));
        return builder.BuildUrl(match.Groups["source"].Value);
    }
}
