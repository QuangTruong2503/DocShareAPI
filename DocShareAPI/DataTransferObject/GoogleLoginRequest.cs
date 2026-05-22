namespace DocShareAPI.DataTransferObject
{
    public class GoogleLoginRequest
    {
        public required string token { get; set; }
        public string? userDevice { get; set; }
    }
}
