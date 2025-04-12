using DocShareAPI.Data;
using DocShareAPI.Models;
using DocShareAPI.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

public class TokenValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TokenServices _tokenServices;
    private readonly IConfiguration _configuration;
    private readonly string[] _publicPaths;
    private readonly IServiceScopeFactory _scopeFactory; // Thêm để truy cập DbContext

    public TokenValidationMiddleware(RequestDelegate next,
                                  IConfiguration configuration,
                                  IServiceScopeFactory scopeFactory) // Thêm IServiceScopeFactory
    {
        _next = next;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _publicPaths = new[]
        {
            "/api/users/public/",
            "/api/verification/public/",
            "/api/categories/public/",
            "/api/public"
        };

        string? tokenSecretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
        if (string.IsNullOrEmpty(tokenSecretKey))
        {
            tokenSecretKey = _configuration.GetValue<string>("TokenSecretKey");
        }

        if (tokenSecretKey == null)
        {
            throw new InvalidOperationException("TokenSecretKey is not configured.");
        }

        _tokenServices = new TokenServices(tokenSecretKey);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLower();
        const string BearerPrefix = "Bearer ";
        string? token = null;
        string? decodedToken = null;
        DecodedTokenResponse? decodedTokenResponse = null;

        // Kiểm tra token và lưu vào context.Items
        if (context.Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
        {
            string authHeader = authorizationHeader.ToString();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    token = authHeader.Substring(BearerPrefix.Length).Trim();
                    decodedToken = _tokenServices.DecodeToken(token);
                    if (decodedToken != null)
                    {
                        decodedTokenResponse = JsonSerializer.Deserialize<DecodedTokenResponse>(decodedToken);
                        if (decodedTokenResponse != null)
                        {
                            context.Items["DecodedToken"] = decodedTokenResponse;
                        }
                    }
                }
                catch (Exception)
                {
                    // Xử lý lỗi
                }
            }
        }

        // Cho phép các public paths đi qua
        if (path != null && _publicPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // Kiểm tra bắt buộc cho các API không public
        if (!context.Request.Headers.TryGetValue("Authorization", out authorizationHeader))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing Authorization header");
            return;
        }

        string authHeaderRequired = authorizationHeader.ToString();
        if (!authHeaderRequired.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid Authorization header format");
            return;
        }

        token = authHeaderRequired.Substring(BearerPrefix.Length).Trim();
        decodedToken = _tokenServices.DecodeToken(token);

        if (decodedToken == null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid token");
            return;
        }

        decodedTokenResponse = JsonSerializer.Deserialize<DecodedTokenResponse>(decodedToken);
        if (decodedTokenResponse == null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Failed to deserialize token");
            return;
        }

        // Kiểm tra token trong database
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DocShareDbContext>(); // Thay YourDbContext bằng tên DbContext của bạn

            var tokenEntity = await dbContext.TOKENS
                .FirstOrDefaultAsync(t => t.token == token && t.is_active);

            if (tokenEntity == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Không tìm thấy token hợp lệ");
                return;
            }

            if (tokenEntity.expires_at < DateTime.UtcNow)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Token đã hết hạn");
                return;
            }
        }

        context.Items["DecodedToken"] = decodedTokenResponse;
        await _next(context);
    }
}

// Extension method giữ nguyên
public static class TokenValidationMiddlewareExtensions
{
    public static IApplicationBuilder UseTokenValidation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TokenValidationMiddleware>();
    }
}