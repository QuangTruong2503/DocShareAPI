using Microsoft.Extensions.Configuration;
using System.Net.Mail;
using System.Threading.Tasks;

namespace DocShareAPI.EmailServices
{
    public class ResetPasswordEmailService : IEmailService
    {
        private readonly IConfiguration _configuration;

        public ResetPasswordEmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            //lấy email từ appsettings.json hoặc biến môi trường
            var fromEmail = _configuration["EmailSettings:FromEmail"] ?? Environment.GetEnvironmentVariable("EMAIL_SETTING_FROM_EMAIL");
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
            var appPassword = _configuration["EmailSettings:AppPassword"] ?? Environment.GetEnvironmentVariable("EMAIL_SETTING_APP_PASSWORD");
            using var smtp = new System.Net.Mail.SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new System.Net.NetworkCredential(
                    fromEmail,
                    appPassword)
            };

            await smtp.SendMailAsync(mailMessage);
        }

        public async Task SendResetPasswordEmailAsync(string toEmail, string recipientName, string resetToken)
        {
            var domain = _configuration["DOMAIN"] ?? Environment.GetEnvironmentVariable("DOMAIN");
            string resetLink = $"{domain}/reset-password/{resetToken}";
            string emailBody = GetResetPasswordEmailTemplate(recipientName, resetLink);
            await SendEmailAsync(toEmail, "Đặt lại mật khẩu của bạn", emailBody);
        }

        private string GetResetPasswordEmailTemplate(string recipientName, string resetLink)
        {
            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        .container {{ font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e0e0e0; border-radius: 5px; }}
                        .button {{ background-color: #FF5733; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block; }}
                        .footer {{ font-size: 12px; color: #666; margin-top: 20px; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <h2>Đặt lại mật khẩu</h2>
                        <p>Xin chào {recipientName},</p>
                        <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu. Nhấp vào nút dưới đây để tiếp tục:</p>
                        <p><a href='{resetLink}' class='button'>Đặt lại mật khẩu</a></p>
                        <strong>Hết hạn sau 3 phút</strong>
                        <p>Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng bỏ qua email này.</p>
                        <p>Link: {resetLink}</p>
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
