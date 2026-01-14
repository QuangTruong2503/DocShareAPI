using DocShareAPI.Services.EmailServices;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DocShareAPI.EmailServices
{
    public class VerifyEmailService : IVerifyEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        public VerifyEmailService(
            IConfiguration configuration,
            HttpClient httpClient)
        {
            _configuration = configuration;
            _httpClient = httpClient;
        }


        public async Task SendVerifyEmailAsync(
            string toEmail,
            string recipientName,
            string verifyToken)
        {
            var apiKey =
                Environment.GetEnvironmentVariable("RESEND_API_KEY")
                ?? _configuration["Resend:ApiKey"];
            var templateId =
                Environment.GetEnvironmentVariable("RESEND_VERIFY_EMAIL_TEMPLATE_ID")
                ?? _configuration["Resend:VerifyEmailTemplateId"];

            var fromEmail =
                Environment.GetEnvironmentVariable("RESEND_FROM_EMAIL")
                ?? _configuration["Resend:FromEmail"];

            var domain =
                Environment.GetEnvironmentVariable("DOMAIN")
                ?? _configuration["DOMAIN"];

            var appName =
                Environment.GetEnvironmentVariable("APP_NAME")
                ?? _configuration["APP_NAME"]
                ?? "DocShare";

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey));

            if (string.IsNullOrWhiteSpace(fromEmail))
                throw new ArgumentNullException(nameof(fromEmail));

            if (string.IsNullOrWhiteSpace(domain))
                throw new ArgumentNullException(nameof(domain));

            string verifyLink = $"{domain}/verify-email/{verifyToken}";

            var payload = new
            {
                from = $"{appName} <{fromEmail}>",
                to = new[] { toEmail },

                // DÙNG TEMPLATE
                template = new
                {
                    id = templateId,
                    variables = new
                    {
                        app_name = appName,
                        user_name = recipientName,
                        verify_url = verifyLink,
                        expire_time = "3 phút"
                    }
                }
            };

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.resend.com/emails"
            );

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Resend template email failed: {error}");
            }
        }
    }
}
