using System.Security.Cryptography;

namespace ELearningAPI.Helpers
{
    public class PasswordHasher
    {
        // Số lần lặp lại thuật toán (nên là một số lớn để tăng độ bảo mật)
        private const int Iterations = 10000;
        // Kích thước của salt
        private const int SaltSize = 16;
        // Kích thước của hash mật khẩu
        private const int HashSize = 20;

        public static string HashPassword(string password)
        {
            // Tạo một salt ngẫu nhiên bằng RandomNumberGenerator
            byte[] salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt); // Thay thế RNGCryptoServiceProvider

            // Hash mật khẩu cùng với salt bằng Rfc2898DeriveBytes
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
            {
                byte[] hash = pbkdf2.GetBytes(HashSize);

                // Kết hợp salt và hash lại thành một mảng byte
                byte[] hashBytes = new byte[SaltSize + HashSize];
                Array.Copy(salt, 0, hashBytes, 0, SaltSize);
                Array.Copy(hash, 0, hashBytes, SaltSize, HashSize);

                // Chuyển đổi mảng thành chuỗi Base64
                return Convert.ToBase64String(hashBytes);
            }
        }
        //Kiểm tra mật khẩu và mã hash trả về true nếu trùng khớp
        public static bool VerifyPassword(string password, string storedHash)
        {
            // Chuyển đổi storedHash từ Base64 thành byte[]
            byte[] hashBytes = Convert.FromBase64String(storedHash);

            // Lấy salt từ hashBytes
            byte[] salt = new byte[SaltSize];
            Array.Copy(hashBytes, 0, salt, 0, SaltSize);

            // Hash mật khẩu nhập vào với salt vừa lấy ra
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
            {
                byte[] hash = pbkdf2.GetBytes(HashSize);

                // So sánh hash của mật khẩu nhập vào với hash đã lưu
                for (int i = 0; i < HashSize; i++)
                {
                    if (hashBytes[i + SaltSize] != hash[i])
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
