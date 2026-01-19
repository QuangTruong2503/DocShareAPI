using DocShareAPI.Data;
using DocShareAPI.Helpers;
using DocShareAPI.Models;
using DocShareAPI.Services;
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
        "/api/document/",
        "/api/users/public/",
        "/api/verification/public/",
        "/api/categories/public/",
        "/api/public"
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
        bool isPublicEndpoint = path != null && _publicPaths.Any(p => path.StartsWith(p));

        DecodedTokenResponse? decodedToken = null;

        // 🔹 1. Nếu có Authorization → decode token (dù public hay private)
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            string headerValue = authHeader.ToString();

            if (!headerValue.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Invalid Authorization header format");
                return;
            }

            string token = headerValue.Substring(BearerPrefix.Length).Trim();

            try
            {
                var decodedJson = _tokenServices.DecodeToken(token);
                if (decodedJson == null)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Invalid token");
                    return;
                }

                decodedToken = JsonSerializer.Deserialize<DecodedTokenResponse>(decodedJson);
                if (decodedToken == null)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Invalid token payload");
                    return;
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
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Token not found or expired");
                    return;
                }

                // ✅ Token hợp lệ → lưu vào context
                context.Items[DecodedTokenKey] = decodedToken;
            }
            catch
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Invalid token");
                return;
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
}
public static class TokenValidationMiddlewareExtensions
{
    public static IApplicationBuilder UseTokenValidation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TokenValidationMiddleware>();
    }
}
