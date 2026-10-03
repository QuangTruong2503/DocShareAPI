namespace DocShareAPI.Helpers
{
    public class GenerateRandomCode
    {

        public static int GenerateID()
        {
            return System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000000, 1000000000); // Số ngẫu nhiên 9 chữ số
        }
        public static string GenerateTwoFactorCode()
        {
            return System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString(); // Mã 6 số
        }
    }
}

