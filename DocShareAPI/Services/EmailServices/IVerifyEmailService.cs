namespace DocShareAPI.Services.EmailServices
{
    public interface IVerifyEmailService
    {
        Task SendVerifyEmailAsync(string toEmail, string recipientName, string verifyToken);
    }
}
