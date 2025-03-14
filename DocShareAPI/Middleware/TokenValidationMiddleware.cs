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

        public TokenValidationMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _configuration = configuration;
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
            var decodedToken = await DecodeAndValidateToken(context, _tokenServices);

            if (decodedToken == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Token không hợp lệ" });
                return;
            }

            // Lưu decoded token vào HttpContext.Items để sử dụng sau
            context.Items["DecodedToken"] = decodedToken;
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
