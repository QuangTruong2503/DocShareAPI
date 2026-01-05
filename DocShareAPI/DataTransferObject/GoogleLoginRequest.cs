namespace DocShareAPI.DataTransferObject
{
    public class GoogleLoginRequest
    {
        public required string Token { get; set; }
        public string? UserDevice { get; set; }
    }
}
