namespace DocShareAPI.Services.EmailServices
{
    public interface IResetPasswordEmailService
    {
        Task SendResetPasswordEmailAsync(
       string toEmail,
       string recipientName,
       string resetToken);
    }
}
