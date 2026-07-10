using GsuTimetablingSystem.Data;
using Xunit;

namespace GsuTimetablingSystem.Tests
{
    public class PasswordHasherTests
    {
        [Fact]
        public void Hash_ThenVerify_SamePassword_ReturnsTrue()
        {
            var hash = PasswordHasher.Hash("1234");

            Assert.True(PasswordHasher.Verify("1234", hash));
        }

        [Fact]
        public void Verify_WrongPassword_ReturnsFalse()
        {
            var hash = PasswordHasher.Hash("1234");

            Assert.False(PasswordHasher.Verify("yanlis-sifre", hash));
        }

        [Fact]
        public void Hash_SamePasswordTwice_ProducesDifferentHashes()
        {
            // Her çağrıda rastgele bir salt üretildiği için aynı şifrenin
            // hash'i her seferinde farklı olmalı (rainbow table saldırılarına karşı).
            var hash1 = PasswordHasher.Hash("1234");
            var hash2 = PasswordHasher.Hash("1234");

            Assert.NotEqual(hash1, hash2);
            Assert.True(PasswordHasher.Verify("1234", hash1));
            Assert.True(PasswordHasher.Verify("1234", hash2));
        }

        [Fact]
        public void IsHashed_DistinguishesHashedFromPlaintext()
        {
            var hash = PasswordHasher.Hash("1234");

            Assert.True(PasswordHasher.IsHashed(hash));
            Assert.False(PasswordHasher.IsHashed("1234"));
        }

        [Fact]
        public void Verify_AgainstRawPlaintextStoredValue_ReturnsFalse()
        {
            // Henüz hash'lenmemiş eski (plaintext) bir satır asla "eşleşti" sayılmamalı;
            // bu tür satırlar HashPlaintextPasswordsAsync tarafından bir sonraki
            // sunucu başlangıcında otomatik olarak hash'e çevrilir.
            Assert.False(PasswordHasher.Verify("1234", "1234"));
        }
    }
}
