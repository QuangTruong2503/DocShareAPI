using DocShareAPI.Models;
using ELearningAPI.Helpers;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace DocShareAPI.Middleware
{
    public class TokenValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly TokenServices _tokenServices;
        private readonly IConfiguration _configuration;
        private readonly string[] _publicPaths; // Danh sách các đường dẫn không cần kiểm tra token

        public TokenValidationMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _configuration = configuration;
            _publicPaths = new[]
        {
            "/api/Users/request-login",
            "/api/Users/request-register",
            "/api/Documents/document/"
        };
            //Lấy dữ liệu TokenKey từ biến môi trường
            string? tokenScretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
            if (string.IsNullOrEmpty(tokenScretKey))
            {
                tokenScretKey = _configuration.GetValue<string>("TokenSecretKey");
            }
            if (tokenScretKey != null)
            {
                _tokenServices = new TokenServices(tokenScretKey);
            }
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Kiểm tra nếu request nằm trong danh sách public paths
            var path = context.Request.Path.Value?.ToLower();
            if (_publicPaths.Any(p => path.StartsWith(p.ToLower())))
            {
                await _next(context); // Bỏ qua kiểm tra token và tiếp tục pipeline
                return;
            }

            // Logic kiểm tra token cho các API không public
            if (!context.Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Missing Authorization header");
                return;
            }

            const string BearerPrefix = "Bearer ";
            if (!authorizationHeader.ToString().StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Invalid Authorization header format");
                return;
            }

            var token = authorizationHeader.ToString().Substring(BearerPrefix.Length).Trim();
            var decodedToken = _tokenServices.DecodeToken(token);

            if (decodedToken == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Invalid token");
                return;
            }

            var decodedTokenResponse = JsonSerializer.Deserialize<DecodedTokenResponse>(decodedToken);
            context.Items["DecodedToken"] = decodedTokenResponse;

            await _next(context);
        }

        private static async Task<DecodedTokenResponse?> DecodeAndValidateToken(HttpContext context, TokenServices tokenServices)
        {
            if (!context.Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
            {
                return null;
            }

            const string BearerPrefix = "Bearer ";
            if (!authorizationHeader.ToString().StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var token = authorizationHeader.ToString().Substring(BearerPrefix.Length).Trim();
            var decodedToken = tokenServices.DecodeToken(token);

            if (decodedToken == null)
            {
                return null;
            }

            return JsonSerializer.Deserialize<DecodedTokenResponse>(decodedToken);
        }
    }

    // Extension method để đăng ký middleware
    public static class TokenValidationMiddlewareExtensions
    {
        public static IApplicationBuilder UseTokenValidation(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<TokenValidationMiddleware>();
        }
    }
}
