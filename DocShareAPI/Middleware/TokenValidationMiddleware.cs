using DocShareAPI.Data;
using DocShareAPI.Helpers;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

public class TokenValidationMiddleware
{
    private const string BearerPrefix = "Bearer ";
    private const string DecodedTokenKey = "DecodedToken";

    private readonly RequestDelegate _next;
    private readonly TokenServices _tokenServices;
    private readonly IServiceScopeFactory _scopeFactory;

    // Các route cho phép anonymous
    private readonly string[] _publicPaths =
    {
        "/api/users/public/",
        "/api/verification/public/",
        "/api/tags/public/",
        "/api/public/"
    };

    public TokenValidationMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory)
    {
        _next = next;
        _scopeFactory = scopeFactory;

        string? tokenSecretKey =
            Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
            ?? configuration.GetValue<string>("TokenSecretKey");

        if (string.IsNullOrWhiteSpace(tokenSecretKey))
            throw new InvalidOperationException("TokenSecretKey is not configured.");

        _tokenServices = new TokenServices(tokenSecretKey);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLower();
        bool allowsAnonymous = context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() != null;
        bool isPublicEndpoint = allowsAnonymous
            || path is "/" or "/api" or "/api/public"
            || path != null && _publicPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        DecodedTokenResponse? decodedToken = null;

        // Public endpoints can be used anonymously. If a stale/bad token is sent,
        // ignore it and let the controller handle the request as a guest.
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            string headerValue = authHeader.ToString();

            if (!TryGetBearerToken(headerValue, out string token))
            {
                if (!isPublicEndpoint)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Invalid Authorization header format");
                    return;
                }
            }
            else
            {
                try
                {
                    var decodedJson = _tokenServices.DecodeToken(token);
                    if (decodedJson == null)
                    {
                        throw new UnauthorizedAccessException("Invalid token");
                    }

                    decodedToken = JsonSerializer.Deserialize<DecodedTokenResponse>(decodedJson);
                    if (decodedToken == null)
                    {
                        throw new UnauthorizedAccessException("Invalid token payload");
                    }

                    // 🔹 2. Kiểm tra token trong DB
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<DocShareDbContext>();

                    var tokenRecords = await dbContext.TOKENS
                        .AsNoTracking()
                        .Where(t =>
                            t.type == TokenType.Access &&
                            t.user_id == decodedToken.userID &&
                            t.is_active &&
                            t.expires_at > DateTime.UtcNow)
                        .ToListAsync();

                    bool valid = tokenRecords.Any(t =>
                        TokenHasher.VerifyToken(token, t.token));

                    if (!valid)
                    {
                        throw new UnauthorizedAccessException("Token not found or expired");
                    }

                    // ✅ Token hợp lệ → lưu vào context
                    context.Items[DecodedTokenKey] = decodedToken;
                }
                catch
                {
                    decodedToken = null;
                    context.Items.Remove(DecodedTokenKey);

                    if (!isPublicEndpoint)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsync("Invalid token");
                        return;
                    }
                }
            }
        }

        // 🔹 3. Nếu endpoint PRIVATE mà KHÔNG có token → 401
        if (!isPublicEndpoint && decodedToken == null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Authentication required");
            return;
        }

        // 🔹 4. Cho request đi tiếp
        await _next(context);
    }

    private static bool TryGetBearerToken(string headerValue, out string token)
    {
        token = string.Empty;

        if (!headerValue.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        token = headerValue.Substring(BearerPrefix.Length).Trim();
        return !string.IsNullOrWhiteSpace(token);
    }
}
public static class TokenValidationMiddlewareExtensions
{
    public static IApplicationBuilder UseTokenValidation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TokenValidationMiddleware>();
    }
}
