using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class SeoSettings
    {
        [Key]
        public int id { get; set; } = 1;
        public string site_name { get; set; } = "DocShare";
        public string site_url { get; set; } = "https://docshare.vn";
        public string default_title { get; set; } = "DocShare - Nền tảng chia sẻ tài liệu học tập";
        public string default_description { get; set; } = "DocShare là nền tảng lưu trữ, tìm kiếm và chia sẻ tài liệu học tập miễn phí.";
        public string default_image { get; set; } = "https://docshare.vn/og-image.svg";
        public string locale { get; set; } = "vi_VN";
        public string robots_txt { get; set; } = "User-agent: *\nAllow: /\nDisallow: /admin\n\nSitemap: https://docshare.vn/sitemap.xml";
        public string sitemap_routes { get; set; } = "[]";
        public DateTime updated_at { get; set; } = DateTime.UtcNow;
    }
}
