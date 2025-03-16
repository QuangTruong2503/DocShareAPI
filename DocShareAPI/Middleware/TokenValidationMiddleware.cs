using DocShareAPI.Models;
using DocShareAPI.Services;
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
                "/api/users/request-login",
                "/api/users/request-register",
                "/api/documents/document/" // Đường dẫn public, bao gồm /api/documents/document/{documentID}
            };

            // Lấy dữ liệu TokenKey từ biến môi trường hoặc configuration
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
            string? token = null; // Khai báo biến ở phạm vi chung
            string? decodedToken = null; // Khai báo biến ở phạm vi chung
            DecodedTokenResponse? decodedTokenResponse = null; // Khai báo biến ở phạm vi chung

            // Kiểm tra token (nếu có) và lưu vào HttpContext.Items, nhưng không chặn request
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
                    catch (ArgumentOutOfRangeException)
                    {
                        // Xử lý khi header không đủ dài để cắt
                    }
                    catch (JsonException)
                    {
                        // Xử lý khi JSON không hợp lệ
                    }
                    catch (Exception)
                    {
                        // Xử lý lỗi chung từ _tokenServices.DecodeToken
                    }
                }
            }

            // Nếu request nằm trong danh sách public paths, tiếp tục pipeline mà không kiểm tra bắt buộc
            if (_publicPaths.Any(p => path.StartsWith(p.ToLower())))
            {
                await _next(context);
                return;
            }

            // Logic kiểm tra token bắt buộc cho các API không public
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

            context.Items["DecodedToken"] = decodedTokenResponse;

            await _next(context);
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