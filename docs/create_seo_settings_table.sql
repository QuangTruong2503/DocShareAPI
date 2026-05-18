CREATE TABLE IF NOT EXISTS `SEO_SETTINGS` (
  `id` int NOT NULL,
  `site_name` text NOT NULL,
  `site_url` text NOT NULL,
  `default_title` text NOT NULL,
  `default_description` text NOT NULL,
  `default_image` text NOT NULL,
  `locale` varchar(20) NOT NULL DEFAULT 'vi_VN',
  `robots_txt` longtext NOT NULL,
  `sitemap_routes` json NOT NULL,
  `updated_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  CONSTRAINT `CK_SEO_SETTINGS_single_row` CHECK (`id` = 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `SEO_SETTINGS` (
  `id`,
  `site_name`,
  `site_url`,
  `default_title`,
  `default_description`,
  `default_image`,
  `locale`,
  `robots_txt`,
  `sitemap_routes`
)
VALUES (
  1,
  'DocShare',
  'https://docshare.vn',
  'DocShare - Nền tảng chia sẻ tài liệu học tập',
  'DocShare là nền tảng lưu trữ, tìm kiếm và chia sẻ tài liệu học tập miễn phí.',
  'https://docshare.vn/og-image.svg',
  'vi_VN',
  'User-agent: *\nAllow: /\nDisallow: /admin\n\nSitemap: https://docshare.vn/sitemap.xml',
  JSON_ARRAY()
)
ON DUPLICATE KEY UPDATE `id` = `id`;
