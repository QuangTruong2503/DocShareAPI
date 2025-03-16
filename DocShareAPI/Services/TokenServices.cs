using DocShareAPI.Models;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace DocShareAPI.Services
{
    public class TokenServices
    {
        private readonly string _secretKey;
        public TokenServices(string secretKey)
        {
            _secretKey = secretKey;
        }
        public string GenerateToken(string userID, string role)
        {
            var claims = new List<Claim>()
            {
                new Claim("userID", userID),
                new Claim("roleID", role),
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.Now.AddDays(1),
                issuer: "DocShare",
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        public string? DecodeToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secretKey);
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateLifetime = true,  // Vẫn kiểm tra thời gian hết hạn của token
                ValidateIssuer = false,  // Bỏ qua kiểm tra Issuer
                ValidateAudience = false  // Bỏ qua kiểm tra Audience
            };

            try
            {
                var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken validatedToken);

                // Chuyển đổi các claim sang dictionary
                var claimsDictionary = principal.Claims.ToDictionary(c => c.Type, c => c.Value);

                // Serialize dictionary thành JSON
                return JsonConvert.SerializeObject(claimsDictionary);
            }
            catch
            {
                // Token không hợp lệ
                return null;
            }
        }


    }
}
