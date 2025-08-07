namespace Server.Authentication
{
    using System.Security.Cryptography;
    using System.Text;

    public static class PasswordHasher
    {
        public static string HashPassword(string password)
        {
            // Generate a random 16-byte salt
            byte[] salt = RandomNumberGenerator.GetBytes(16);

            // Derive a 32-byte key using PBKDF2 with SHA256
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
            byte[] hash = pbkdf2.GetBytes(32);

            // Combine salt + hash and Base64 encode
            byte[] combined = new byte[salt.Length + hash.Length];
            Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
            Buffer.BlockCopy(hash, 0, combined, salt.Length, hash.Length);

            return Convert.ToBase64String(combined);
        }

        public static bool VerifyPassword(string password, string storedHash)
        {
            byte[] combined = Convert.FromBase64String(storedHash);
            if (combined.Length != 48)
                return false;

            byte[] salt = new byte[16];
            byte[] hash = new byte[32];

            Buffer.BlockCopy(combined, 0, salt, 0, 16);
            Buffer.BlockCopy(combined, 16, hash, 0, 32);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
            byte[] attemptedHash = pbkdf2.GetBytes(32);

            return CryptographicOperations.FixedTimeEquals(attemptedHash, hash);
        }
    }

}
