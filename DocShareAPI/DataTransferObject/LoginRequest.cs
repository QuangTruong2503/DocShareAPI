namespace DocShareAPI.DataTransferObject
{
    public class LoginRequest
    {
        public required string Email { get; set; }
        public required string Password { get; set; }

        public string? UserDevice { get; set; }
    }
}
