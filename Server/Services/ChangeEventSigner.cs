using System.Security.Cryptography;
using System.Text;

namespace Server.Services
{
    public static class ChangeEventSigner
    {
        public static string GenerateSecretBase64(int bytes = 32)
        {
            var buf = new byte[bytes];
            RandomNumberGenerator.Fill(buf);
            return Convert.ToBase64String(buf);
        }

        public static string ComputeSignature(string secretBase64, string timestamp, string body)
        {
            var key = Convert.FromBase64String(secretBase64);
            var canonical = $"{timestamp}.{body}";
            using var hmac = new HMACSHA256(key);
            var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            var sb = new StringBuilder(mac.Length * 2);
            foreach (var b in mac) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
