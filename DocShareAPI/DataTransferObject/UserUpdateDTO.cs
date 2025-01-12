namespace DocShareAPI.DataTransferObject
{
    public class UserUpdateDTO
    {
        public Guid user_id { get; set; }
        public required string full_name { get; set; }
        public required string email { get; set; }
        public required string username { get; set; }
    }
}
