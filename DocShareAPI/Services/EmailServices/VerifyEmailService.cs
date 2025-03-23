using Microsoft.Extensions.Configuration;
using System.Net.Mail;
using System.Threading.Tasks;

namespace DocShareAPI.EmailServices
{
    public class VerifyEmailService : IEmailService
    {
        private readonly IConfiguration _configuration;

        public VerifyEmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var fromEmail = Environment.GetEnvironmentVariable("EMAIL_SETTING_FROM_EMAIL") ?? _configuration["EmailSettings:FromEmail"];
            if (string.IsNullOrEmpty(fromEmail))
            {
                throw new ArgumentNullException(nameof(fromEmail), "From email address cannot be null or empty.");
            }

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail, "DocShare"),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            mailMessage.To.Add(toEmail);
            var appPassword = Environment.GetEnvironmentVariable("EMAIL_SETTING_APP_PASSWORD") ?? _configuration["EmailSettings:AppPassword"];
            using var smtp = new System.Net.Mail.SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new System.Net.NetworkCredential(
                    fromEmail,
                    _configuration["EmailSettings:AppPassword"])
            };

            await smtp.SendMailAsync(mailMessage);
        }


        public async Task SendVerificationEmailAsync(string toEmail, string recipientName, string verificationToken)
        {
            var domain = _configuration["DOMAIN"] ?? Environment.GetEnvironmentVariable("DOMAIN"); 
            if (string.IsNullOrEmpty(domain))
            {
                domain = Environment.GetEnvironmentVariable("DOMAIN");
            }
            string verificationLink = $"{domain}/verify-email/{verificationToken}";
            string emailBody = GetVerificationEmailTemplate(recipientName, verificationLink);
            await SendEmailAsync(toEmail, "Xác thực tài khoản của bạn", emailBody);
        }

        private string GetVerificationEmailTemplate(string recipientName, string verificationLink)
        {
            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        .container {{ font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e0e0e0; border-radius: 5px; }}
                        .button {{ background-color: #5ac8fa; color: #fff; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block; }}
                        .footer {{ font-size: 12px; color: #666; margin-top: 20px; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <h2>Xác thực email của bạn</h2>
                        <p>Xin chào {recipientName},</p>
                        <p>Cảm ơn bạn đã đăng ký! Vui lòng nhấp vào nút dưới đây để xác thực địa chỉ email của bạn:</p>
                        <p><a href='{verificationLink}' class='button'>Xác thực ngay</a></p>
                        <p>Nếu nút không hoạt động, bạn có thể sao chép và dán link này vào trình duyệt:</p>
                        <p>{verificationLink}</p>
                        <div class='footer'>
                            <p>Đây là email tự động, vui lòng không trả lời.</p>
                            <p>© 2025 DocShare</p>
                        </div>
                    </div>
                </body>
                </html>";
        }
    }
}
