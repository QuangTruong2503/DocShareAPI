namespace DocShareAPI.Models
{
    public class DecodedTokenResponse
    {
        public  Guid userID { get; set; }
        public  string? roleID { get; set; }
        public  string? exp { get; set; }
        public  string? iss { get; set; }
    }
}
