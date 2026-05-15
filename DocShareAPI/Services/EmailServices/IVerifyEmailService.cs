namespace DocShareAPI.Services.EmailServices
{
    public interface IVerifyEmailService
    {
        Task SendVerifyEmailAsync(string toEmail, string recipientName, string verifyToken);
        Task SendChangeEmailConfirmationAsync(string toEmail, string recipientName, string verifyToken);
    }
}
