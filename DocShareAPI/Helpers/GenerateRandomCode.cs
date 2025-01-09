namespace DocShareAPI.Helpers
{
    public class GenerateRandomCode
    {
        private static Random random = new Random(DateTime.Now.Ticks.GetHashCode());
        public static int GenerateID()
        {
            return random.Next(100000000, 1000000000); // Số ngẫu nhiên 9 chữ số
        }
        
    }
}

