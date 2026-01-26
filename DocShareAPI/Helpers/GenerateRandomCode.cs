namespace DocShareAPI.Helpers
{
    public class GenerateRandomCode
    {
        private static Random random = new Random(DateTime.Now.Ticks.GetHashCode());
        public static int GenerateID()
        {
            return random.Next(100000000, 1000000000); // Số ngẫu nhiên 9 chữ số
        }
        public static string GenerateTwoFactorCode()
        {
            var random = new Random();
            return random.Next(100000, 999999).ToString(); // Mã 6 số
        }
    }
}

