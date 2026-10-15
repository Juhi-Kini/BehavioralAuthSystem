using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BehavioralAuthSystem.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BehavioralAuthSystem.Tests
{
    public class EncryptionServiceTests
    {
        private class TestHostEnvironment : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = Environments.Development;
            public string ApplicationName { get; set; } = "BehavioralAuthSystem";
            public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private readonly IEncryptionService _encryptionService;
        private readonly byte[] _userSalt;

        public EncryptionServiceTests()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MasterEncryptionSecret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                })
                .Build();

            var env = new TestHostEnvironment();
            _encryptionService = new EncryptionService(config, env, NullLogger<EncryptionService>.Instance);
            _userSalt = _encryptionService.GenerateSalt();
        }

        [Fact]
        public void GenerateSalt_ReturnsExactly32RandomBytes()
        {
            byte[] salt1 = _encryptionService.GenerateSalt();
            byte[] salt2 = _encryptionService.GenerateSalt();

            Assert.Equal(32, salt1.Length);
            Assert.Equal(32, salt2.Length);
            Assert.NotEqual(salt1, salt2);
        }

        [Fact]
        public void DeriveUserFileKey_Produces32ByteKey()
        {
            byte[] key = _encryptionService.DeriveUserFileKey(_userSalt);

            Assert.Equal(32, key.Length);
        }

        [Fact]
        public void EncryptAndDecrypt_RoundTrip_RestoresOriginalPlaintext()
        {
            byte[] original = Encoding.UTF8.GetBytes("Confidential vault document payload 12345!@#$%");

            byte[] encrypted = _encryptionService.EncryptData(_userSalt, original);
            byte[] decrypted = _encryptionService.DecryptData(_userSalt, encrypted);

            Assert.NotEqual(original, encrypted);
            Assert.Equal(original, decrypted);
            Assert.Equal("Confidential vault document payload 12345!@#$%", Encoding.UTF8.GetString(decrypted));
        }

        [Fact]
        public void Decrypt_WithTamperedCiphertext_ThrowsCryptographicException()
        {
            byte[] original = Encoding.UTF8.GetBytes("Top secret document bytes.");
            byte[] encrypted = _encryptionService.EncryptData(_userSalt, original);

            // Tamper with ciphertext byte (after 12-byte nonce and 16-byte tag, offset >= 28)
            encrypted[28] ^= 0xFF;

            Assert.ThrowsAny<CryptographicException>(() =>
            {
                _encryptionService.DecryptData(_userSalt, encrypted);
            });
        }

        [Fact]
        public void Decrypt_WithTamperedTag_ThrowsCryptographicException()
        {
            byte[] original = Encoding.UTF8.GetBytes("Data integrity test.");
            byte[] encrypted = _encryptionService.EncryptData(_userSalt, original);

            // Tamper with authentication tag (bytes 12 to 27)
            encrypted[15] ^= 0xAA;

            Assert.ThrowsAny<CryptographicException>(() =>
            {
                _encryptionService.DecryptData(_userSalt, encrypted);
            });
        }

        [Fact]
        public void Decrypt_WithWrongUserSalt_ThrowsCryptographicException()
        {
            byte[] original = Encoding.UTF8.GetBytes("Cross-user data isolation test.");
            byte[] encrypted = _encryptionService.EncryptData(_userSalt, original);

            byte[] otherUserSalt = _encryptionService.GenerateSalt();

            Assert.ThrowsAny<CryptographicException>(() =>
            {
                _encryptionService.DecryptData(otherUserSalt, encrypted);
            });
        }
    }
}
