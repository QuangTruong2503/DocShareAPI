namespace DocShareAPI.DataTransferObject
{
    public class GoogleLoginRequest
    {
        public required string token { get; set; }
        public string? userDevice { get; set; }
        // Explicit confirmation before binding Google to an existing password account.
        public string? linkPassword { get; set; }
    }
}
