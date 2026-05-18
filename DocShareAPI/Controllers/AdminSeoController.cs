using DocShareAPI.Data;
using DocShareAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace DocShareAPI.Controllers
{
    [Route("api/admin/seo")]
    [ApiController]
    public class AdminSeoController : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private static readonly HashSet<string> ChangeFrequencies = new(StringComparer.OrdinalIgnoreCase)
        {
            "always", "hourly", "daily", "weekly", "monthly", "yearly", "never"
        };

        private readonly DocShareDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public AdminSeoController(DocShareDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var settings = await GetOrCreateSettings();
            return Ok(new { data = ToSettingsResponse(settings) });
        }

        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings([FromBody] SeoSettingsRequest request)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var validationErrors = ValidateSettingsRequest(request);
            if (validationErrors.Count > 0)
                return BadRequest(new { message = "Dữ liệu SEO không hợp lệ.", errors = validationErrors });

            var settings = await GetOrCreateSettings();
            settings.site_name = request.SiteName!.Trim();
            settings.site_url = NormalizeSiteUrl(request.SiteUrl!);
            settings.default_title = request.DefaultTitle!.Trim();
            settings.default_description = request.DefaultDescription!.Trim();
            settings.default_image = request.DefaultImage!.Trim();
            settings.locale = string.IsNullOrWhiteSpace(request.Locale) ? "vi_VN" : request.Locale.Trim();
            settings.updated_at = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(new { data = ToSettingsResponse(settings) });
        }

        [HttpGet("robots")]
        public async Task<IActionResult> GetRobots()
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var settings = await GetOrCreateSettings();
            return Ok(new { data = new { content = settings.robots_txt } });
        }

        [HttpPut("robots")]
        public async Task<IActionResult> UpdateRobots([FromBody] RobotsRequest request)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            if (request == null || string.IsNullOrWhiteSpace(request.Content))
                return BadRequest(new { message = "Nội dung robots.txt là bắt buộc.", errors = new[] { "content is required." } });

            var settings = await GetOrCreateSettings();
            settings.robots_txt = NormalizeLineEndings(request.Content.Trim());
            settings.updated_at = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await WritePublicFile("robots.txt", settings.robots_txt);
            return Ok(new { data = new { content = settings.robots_txt } });
        }

        [HttpGet("sitemap-routes")]
        public async Task<IActionResult> GetSitemapRoutes()
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            var settings = await GetOrCreateSettings();
            return Ok(new { data = ParseRoutes(settings.sitemap_routes) });
        }

        [HttpPut("sitemap-routes")]
        public async Task<IActionResult> UpdateSitemapRoutes([FromBody] SitemapRoutesRequest request)
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            if (request == null || request.Routes.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return BadRequest(new { message = "Danh sách routes là bắt buộc.", errors = new[] { "routes is required." } });

            var routeRequests = ParseRouteRequests(request.Routes);
            if (routeRequests == null)
                return BadRequest(new { message = "Danh sách routes không hợp lệ.", errors = new[] { "routes must be an array of strings or objects." } });

            var routes = routeRequests
                .Select(NormalizeRoute)
                .Where(route => route != null && !IsPrivateRoute(route.Path))
                .GroupBy(route => route!.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()!)
                .ToList();

            var settings = await GetOrCreateSettings();
            settings.sitemap_routes = JsonSerializer.Serialize(routes, JsonOptions);
            settings.updated_at = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { data = routes });
        }

        [HttpPost("sitemap/generate")]
        public async Task<IActionResult> GenerateSitemap()
        {
            if (!TryRequireAdmin(out var error))
                return error!;

            try
            {
                var settings = await GetOrCreateSettings();
                var generatedAt = DateTime.UtcNow;
                var urls = new Dictionary<string, SitemapUrl>(StringComparer.OrdinalIgnoreCase);

                foreach (var route in ParseRoutes(settings.sitemap_routes))
                {
                    AddUrl(urls, new SitemapUrl(route.Path, generatedAt, route.Changefreq, route.Priority));
                }

                var documents = await _context.DOCUMENTS
                    .AsNoTracking()
                    .Where(d => d.is_public)
                    .Select(d => new { d.document_id, d.uploaded_at })
                    .ToListAsync();

                foreach (var document in documents)
                {
                    AddUrl(urls, new SitemapUrl($"/document/{document.document_id}", document.uploaded_at, "weekly", 0.8m));
                }

                var userIds = await _context.USERS
                    .AsNoTracking()
                    .Where(u =>
                        _context.DOCUMENTS.Any(d => d.user_id == u.user_id && d.is_public) ||
                        _context.COLLECTIONS.Any(c => c.user_id == u.user_id && c.is_public))
                    .Select(u => u.user_id)
                    .ToListAsync();

                foreach (var userId in userIds)
                {
                    AddUrl(urls, new SitemapUrl($"/public-profile/{userId}", generatedAt, "weekly", 0.6m));
                }

                var collections = await _context.COLLECTIONS
                    .AsNoTracking()
                    .Where(c => c.is_public)
                    .Select(c => new { c.collection_id, c.created_at })
                    .ToListAsync();

                foreach (var collection in collections)
                {
                    AddUrl(urls, new SitemapUrl($"/collection/{collection.collection_id}", collection.created_at, "weekly", 0.7m));
                }

                var categories = await _context.CATEGORIES
                    .AsNoTracking()
                    .Select(c => c.category_id)
                    .ToListAsync();

                foreach (var categoryId in categories)
                {
                    AddUrl(urls, new SitemapUrl($"/category/{categoryId}", generatedAt, "weekly", 0.6m));
                }

                var xml = BuildSitemapXml(settings.site_url, urls.Values.OrderBy(u => u.Path).ToList());
                await WritePublicFile("sitemap.xml", xml);

                return Ok(new
                {
                    data = new
                    {
                        generatedAt,
                        urlCount = urls.Count,
                        sitemapUrl = $"{settings.site_url}/sitemap.xml"
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể generate sitemap.xml.", errors = new[] { ex.Message } });
            }
        }

        private async Task<SeoSettings> GetOrCreateSettings()
        {
            var settings = await _context.SEO_SETTINGS.FirstOrDefaultAsync(s => s.id == 1);
            if (settings != null)
                return settings;

            settings = new SeoSettings();
            _context.SEO_SETTINGS.Add(settings);
            await _context.SaveChangesAsync();
            return settings;
        }

        private bool TryRequireAdmin(out IActionResult? error)
        {
            var decodedToken = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                error = Unauthorized(new { message = "Bạn cần đăng nhập để truy cập chức năng SEO.", errors = Array.Empty<string>() });
                return false;
            }

            if (!string.Equals(decodedToken.roleID, "admin", StringComparison.OrdinalIgnoreCase))
            {
                error = StatusCode(StatusCodes.Status403Forbidden, new { message = "Bạn không có quyền truy cập chức năng SEO.", errors = Array.Empty<string>() });
                return false;
            }

            error = null;
            return true;
        }

        private static object ToSettingsResponse(SeoSettings settings)
        {
            return new
            {
                siteName = settings.site_name,
                siteUrl = settings.site_url,
                defaultTitle = settings.default_title,
                defaultDescription = settings.default_description,
                defaultImage = settings.default_image,
                locale = settings.locale
            };
        }

        private static List<string> ValidateSettingsRequest(SeoSettingsRequest request)
        {
            var errors = new List<string>();
            if (request == null)
            {
                errors.Add("request body is required.");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(request.SiteName))
                errors.Add("siteName is required.");
            if (!IsValidAbsoluteUrl(request.SiteUrl, out _))
                errors.Add("siteUrl must be a valid absolute URL.");
            if (string.IsNullOrWhiteSpace(request.DefaultTitle))
                errors.Add("defaultTitle is required.");
            else if (request.DefaultTitle.Trim().Length > 70)
                errors.Add("defaultTitle must be at most 70 characters.");
            if (string.IsNullOrWhiteSpace(request.DefaultDescription))
                errors.Add("defaultDescription is required.");
            else if (request.DefaultDescription.Trim().Length > 180)
                errors.Add("defaultDescription must be at most 180 characters.");
            if (!IsValidAbsoluteUrl(request.DefaultImage, out _))
                errors.Add("defaultImage must be a valid absolute URL.");

            return errors;
        }

        private static bool IsValidAbsoluteUrl(string? url, out Uri? uri)
        {
            uri = null;
            return !string.IsNullOrWhiteSpace(url)
                && Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private static string NormalizeSiteUrl(string siteUrl)
        {
            var normalized = siteUrl.Trim().TrimEnd('/');
            return normalized;
        }

        private static string NormalizeLineEndings(string content)
        {
            return content.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static SitemapRoute? NormalizeRoute(SitemapRouteRequest route)
        {
            var path = NormalizePath(route.Path);
            if (path == null)
                return null;

            var priority = route.Priority ?? (path == "/" ? 1.0m : 0.8m);
            priority = Math.Clamp(priority, 0.0m, 1.0m);

            var changefreq = string.IsNullOrWhiteSpace(route.Changefreq)
                ? (path == "/" ? "daily" : "weekly")
                : route.Changefreq.Trim().ToLowerInvariant();

            if (!ChangeFrequencies.Contains(changefreq))
                changefreq = path == "/" ? "daily" : "weekly";

            return new SitemapRoute(path, priority, changefreq);
        }

        private static string? NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var normalized = path.Trim();
            var queryIndex = normalized.IndexOfAny(new[] { '?', '#' });
            if (queryIndex >= 0)
                normalized = normalized[..queryIndex];

            if (!normalized.StartsWith('/'))
                normalized = "/" + normalized;

            return normalized.Length > 1 ? normalized.TrimEnd('/') : "/";
        }

        private static List<SitemapRouteRequest>? ParseRouteRequests(JsonElement routes)
        {
            if (routes.ValueKind != JsonValueKind.Array)
                return null;

            var parsedRoutes = new List<SitemapRouteRequest>();
            foreach (var route in routes.EnumerateArray())
            {
                if (route.ValueKind == JsonValueKind.String)
                {
                    parsedRoutes.Add(new SitemapRouteRequest { Path = route.GetString() });
                    continue;
                }

                if (route.ValueKind == JsonValueKind.Object)
                {
                    parsedRoutes.Add(new SitemapRouteRequest
                    {
                        Path = TryGetString(route, "path"),
                        Priority = TryGetDecimal(route, "priority"),
                        Changefreq = TryGetString(route, "changefreq")
                    });
                    continue;
                }

                return null;
            }

            return parsedRoutes;
        }

        private static string? TryGetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }

        private static decimal? TryGetDecimal(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var value)
                ? value
                : null;
        }

        private static bool IsPrivateRoute(string path)
        {
            var lower = path.ToLowerInvariant();
            return lower == "/admin" ||
                lower.StartsWith("/admin/") ||
                lower == "/account" ||
                lower.StartsWith("/account/") ||
                lower == "/my-documents" ||
                lower.StartsWith("/my-documents/") ||
                lower == "/my-collections" ||
                lower.StartsWith("/my-collections/") ||
                lower == "/my-reports" ||
                lower.StartsWith("/my-reports/") ||
                lower.Contains("reset-password") ||
                lower.Contains("verify-email") ||
                lower.Contains("token");
        }

        private static List<SitemapRoute> ParseRoutes(string? routesJson)
        {
            if (string.IsNullOrWhiteSpace(routesJson))
                return new List<SitemapRoute>();

            try
            {
                return JsonSerializer.Deserialize<List<SitemapRoute>>(routesJson, JsonOptions) ?? new List<SitemapRoute>();
            }
            catch
            {
                return new List<SitemapRoute>();
            }
        }

        private static void AddUrl(Dictionary<string, SitemapUrl> urls, SitemapUrl url)
        {
            if (!IsPrivateRoute(url.Path))
                urls[url.Path] = url;
        }

        private static string BuildSitemapXml(string siteUrl, IReadOnlyCollection<SitemapUrl> urls)
        {
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            var root = new XElement(ns + "urlset",
                urls.Select(url => new XElement(ns + "url",
                    new XElement(ns + "loc", $"{siteUrl}{url.Path}"),
                    new XElement(ns + "lastmod", url.LastModified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    new XElement(ns + "changefreq", url.Changefreq),
                    new XElement(ns + "priority", url.Priority.ToString("0.0", CultureInfo.InvariantCulture)))));

            var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
            var builder = new StringBuilder();
            builder.AppendLine(document.Declaration!.ToString());
            builder.Append(document);
            return builder.ToString();
        }

        private async Task WritePublicFile(string fileName, string content)
        {
            var webRootPath = _environment.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRootPath))
                webRootPath = Path.Combine(_environment.ContentRootPath, "wwwroot");

            Directory.CreateDirectory(webRootPath);
            var filePath = Path.Combine(webRootPath, fileName);
            await System.IO.File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
        }

        public class SeoSettingsRequest
        {
            public string? SiteName { get; set; }
            public string? SiteUrl { get; set; }
            public string? DefaultTitle { get; set; }
            public string? DefaultDescription { get; set; }
            public string? DefaultImage { get; set; }
            public string? Locale { get; set; }
        }

        public class RobotsRequest
        {
            public string? Content { get; set; }
        }

        public class SitemapRoutesRequest
        {
            public JsonElement Routes { get; set; }
        }

        public class SitemapRouteRequest
        {
            public string? Path { get; set; }
            public decimal? Priority { get; set; }
            public string? Changefreq { get; set; }
        }

        public record SitemapRoute(string Path, decimal Priority, string Changefreq);
        private record SitemapUrl(string Path, DateTime LastModified, string Changefreq, decimal Priority);
    }
}
