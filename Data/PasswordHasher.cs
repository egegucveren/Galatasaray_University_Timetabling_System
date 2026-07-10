using System.Security.Cryptography;
using System.Text;

namespace GsuTimetablingSystem.Data
{
    /// <summary>
    /// Şifreleri artık veritabanında düz metin (plaintext) olarak DEĞİL, PBKDF2 (SHA-256,
    /// rastgele salt, 100.000 iterasyon) ile hash'lenmiş olarak saklıyoruz.
    /// Depolanan format: "PBKDF2$&lt;iterasyon&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;"
    /// Bu format sayesinde eski (henüz hash'lenmemiş) satırlar kolayca ayırt edilip
    /// tek seferlik bir geçiş adımıyla (bkz. MySqlScheduleRepository.InitializeAsync)
    /// otomatik olarak hash'e çevrilebiliyor; giriş yapan kullanıcının kullandığı
    /// şifre hiç değişmiyor, sadece veritabanındaki saklama biçimi güvenli hale geliyor.
    /// </summary>
    public static class PasswordHasher
    {
        private const string Prefix = "PBKDF2$";
        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;
        private const int Iterations = 100_000;

        public static bool IsHashed(string storedValue) =>
            !string.IsNullOrEmpty(storedValue) && storedValue.StartsWith(Prefix, StringComparison.Ordinal);

        public static string Hash(string plainPassword)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(plainPassword),
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSizeBytes);

            return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        public static bool Verify(string plainPassword, string storedValue)
        {
            if (!IsHashed(storedValue)) return false;

            var parts = storedValue.Substring(Prefix.Length).Split('$');
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[0], out var iterations)) return false;

            byte[] salt, expectedHash;
            try
            {
                salt = Convert.FromBase64String(parts[1]);
                expectedHash = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return false;
            }

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(plainPassword),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
    }
}
