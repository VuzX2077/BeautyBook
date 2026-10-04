using System.Security.Cryptography;
using System.Text;

namespace BeautyBookBackend.Services;

public static class PasswordHasher
{
        public static string Hash(string password)
        {
            const int iterations = 210_000;
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
            return $"PBKDF2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        public static bool Verify(string password, string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return false;
            if (stored.StartsWith("PBKDF2$", StringComparison.Ordinal))
            {
                var parts = stored.Split('$');
                if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations)) return false;
                try
                {
                    var salt = Convert.FromBase64String(parts[2]);
                    var expected = Convert.FromBase64String(parts[3]);
                    var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                    return CryptographicOperations.FixedTimeEquals(expected, actual);
                }
                catch (FormatException) { return false; }
            }
            using var sha256 = SHA256.Create();
            var legacy = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            try { return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(stored), legacy); }
            catch (FormatException) { return false; }
        }

}
