namespace DocShareAPI.Services.EmailServices
{
    public interface ITwoFactorEmailService
    {
        Task SendTwoFactorCodeAsync(string toEmail, string recipientName, string twoFactorCode);
    }
}
