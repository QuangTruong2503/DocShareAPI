using System.Security.Cryptography;
using System.Text;

namespace DocShareAPI.Helpers
{
    public static class TokenHasher
    {
        /// <summary>
        /// Hash token bằng SHA256 để lưu DB
        /// </summary>
        public static string HashToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Token không hợp lệ");

            using var sha256 = SHA256.Create();
            byte[] tokenBytes = Encoding.UTF8.GetBytes(token);
            byte[] hashBytes = sha256.ComputeHash(tokenBytes);

            return Convert.ToBase64String(hashBytes);
        }

        /// <summary>
        /// So sánh token plain text với token đã hash trong DB
        /// </summary>
        public static bool VerifyToken(string token, string storedHash)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(storedHash))
                return false;

            string tokenHash = HashToken(token);

            // So sánh constant-time để tránh timing attack
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(tokenHash),
                Convert.FromBase64String(storedHash)
            );
        }
    }
}
