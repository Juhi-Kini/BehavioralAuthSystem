using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BehavioralAuthSystem.Services
{
    public interface IEncryptionService
    {
        byte[] GenerateSalt();
        byte[] DeriveUserFileKey(byte[] userSalt);
        byte[] EncryptData(byte[] userSalt, byte[] plaintext);
        byte[] DecryptData(byte[] userSalt, byte[] encryptedData);
        string GetMasterSecretStatus();
    }

    public class EncryptionService : IEncryptionService
    {
        private readonly byte[] _masterSecretBytes;
        private readonly string _masterSecretSource;
        private readonly ILogger<EncryptionService> _logger;

        public EncryptionService(IConfiguration configuration, IHostEnvironment environment, ILogger<EncryptionService> logger)
        {
            _logger = logger;
            var masterSecretFromConfig = configuration["MasterEncryptionSecret"];

            if (string.IsNullOrWhiteSpace(masterSecretFromConfig))
            {
                if (environment.IsDevelopment())
                {
                    // Auto-generate 256-bit key and persist to appsettings.Development.json
                    var randomSecretBytes = RandomNumberGenerator.GetBytes(32);
                    var generatedBase64Secret = Convert.ToBase64String(randomSecretBytes);

                    _masterSecretBytes = randomSecretBytes;
                    _masterSecretSource = "Auto-generated & Persisted in appsettings.Development.json";

                    PersistSecretToDevConfig(environment.ContentRootPath, generatedBase64Secret);
                    _logger.LogInformation("MasterEncryptionSecret was missing in Development. Auto-generated and persisted key to appsettings.Development.json.");
                }
                else
                {
                    throw new InvalidOperationException("CRITICAL SECURITY ERROR: MasterEncryptionSecret is not configured in environment or user-secrets for non-Development runtime. Ephemeral key generation is prohibited in Production.");
                }
            }
            else
            {
                try
                {
                    _masterSecretBytes = Convert.FromBase64String(masterSecretFromConfig);
                    _masterSecretSource = "Configured via Configuration / Environment";
                }
                catch
                {
                    _masterSecretBytes = Encoding.UTF8.GetBytes(masterSecretFromConfig);
                    _masterSecretSource = "Configured via Plaintext String";
                }
                _logger.LogInformation("MasterEncryptionSecret loaded successfully.");
            }
        }

        private static void PersistSecretToDevConfig(string contentRootPath, string base64Secret)
        {
            try
            {
                string devConfigPath = Path.Combine(contentRootPath, "appsettings.Development.json");
                System.Text.Json.Nodes.JsonObject jsonNode;
                if (File.Exists(devConfigPath))
                {
                    string existingJson = File.ReadAllText(devConfigPath);
                    jsonNode = System.Text.Json.Nodes.JsonNode.Parse(existingJson)?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
                }
                else
                {
                    jsonNode = new System.Text.Json.Nodes.JsonObject();
                }

                jsonNode["MasterEncryptionSecret"] = base64Secret;
                File.WriteAllText(devConfigPath, jsonNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Could not persist MasterEncryptionSecret to appsettings.Development.json: {ex.Message}");
            }
        }

        public string GetMasterSecretStatus() => _masterSecretSource;

        public byte[] GenerateSalt()
        {
            return RandomNumberGenerator.GetBytes(32);
        }

        public byte[] DeriveUserFileKey(byte[] userSalt)
        {
            if (userSalt == null || userSalt.Length != 32)
            {
                throw new ArgumentException("User salt must be exactly 32 bytes.", nameof(userSalt));
            }

            var info = Encoding.UTF8.GetBytes("file-encryption-v1");
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, _masterSecretBytes, 32, userSalt, info);
        }

        public byte[] EncryptData(byte[] userSalt, byte[] plaintext)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));

            byte[] key = DeriveUserFileKey(userSalt);
            byte[] nonce = RandomNumberGenerator.GetBytes(12); // 96-bit nonce for AES-GCM
            byte[] tag = new byte[16]; // 128-bit authentication tag
            byte[] ciphertext = new byte[plaintext.Length];

            using (var aesGcm = new AesGcm(key, 16))
            {
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            // Binary layout: [12-byte Nonce][16-byte Tag][Ciphertext...]
            byte[] result = new byte[12 + 16 + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, 12);
            Buffer.BlockCopy(tag, 0, result, 12, 16);
            Buffer.BlockCopy(ciphertext, 0, result, 28, ciphertext.Length);

            return result;
        }

        public byte[] DecryptData(byte[] userSalt, byte[] encryptedData)
        {
            if (encryptedData == null || encryptedData.Length < 28)
            {
                throw new CryptographicException("Invalid encrypted payload. Header metadata missing.");
            }

            byte[] key = DeriveUserFileKey(userSalt);

            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            int ciphertextLength = encryptedData.Length - 28;
            byte[] ciphertext = new byte[ciphertextLength];

            Buffer.BlockCopy(encryptedData, 0, nonce, 0, 12);
            Buffer.BlockCopy(encryptedData, 12, tag, 0, 16);
            Buffer.BlockCopy(encryptedData, 28, ciphertext, 0, ciphertextLength);

            byte[] plaintext = new byte[ciphertextLength];

            using (var aesGcm = new AesGcm(key, 16))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            return plaintext;
        }
    }
}
