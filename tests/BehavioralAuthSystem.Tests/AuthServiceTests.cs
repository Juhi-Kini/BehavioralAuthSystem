using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BehavioralAuthSystem.Data;
using BehavioralAuthSystem.Models;
using BehavioralAuthSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BehavioralAuthSystem.Tests
{
    public class AuthServiceTests
    {
        private class TestHostEnvironment : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = Environments.Development;
            public string ApplicationName { get; set; } = "BehavioralAuthSystem";
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private static AppDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private static IEncryptionService CreateTestEncryptionService()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MasterEncryptionSecret"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
                })
                .Build();
            return new EncryptionService(config, new TestHostEnvironment(), NullLogger<EncryptionService>.Instance);
        }

        private static KeystrokeSample CreateSample(double hold = 100.0, double lat = 150.0)
        {
            return new KeystrokeSample
            {
                HoldTimes = new List<double> { hold, hold, hold, hold, hold },
                Latencies = new List<double> { lat, lat, lat, lat },
                KeyCount = 5,
                TotalDuration = 5 * hold + 4 * lat
            };
        }

        [Fact]
        public void HashPassword_And_VerifyPassword_Success()
        {
            string password = "SecretMasterPassword123!";
            string hash = AuthService.HashPassword(password);

            Assert.True(AuthService.VerifyPassword(password, hash));
            Assert.False(AuthService.VerifyPassword("WrongPassword!", hash));
        }

        [Fact]
        public void VerifyPassword_ConstantTime_ReturnsFalseForCorruptedHash()
        {
            Assert.False(AuthService.VerifyPassword("password", "NotAValidBase64String"));
            Assert.False(AuthService.VerifyPassword("password", Convert.ToBase64String(new byte[10])));
        }

        [Fact]
        public async Task RegisterAsync_ValidInputs_SucceedsAndEnrollsPatterns()
        {
            var db = CreateInMemoryDbContext();
            var keystroke = new KeystrokeService();
            var enc = CreateTestEncryptionService();
            var auth = new AuthService(db, keystroke, enc, NullLogger<AuthService>.Instance);

            var pwdSamples = new List<KeystrokeSample> { CreateSample(), CreateSample(), CreateSample(), CreateSample() };
            var actSamples = new List<KeystrokeSample> { CreateSample(), CreateSample(), CreateSample(), CreateSample() };

            var (success, msg, user) = await auth.RegisterAsync("alice", "Password123!", "transfer phrase", pwdSamples, actSamples);

            Assert.True(success);
            Assert.NotNull(user);
            Assert.Equal("alice", user.Username);

            var userPatterns = await db.BehavioralPatterns.Where(p => p.UserId == user.Id).ToListAsync();
            Assert.Equal(2, userPatterns.Count);
        }

        [Fact]
        public async Task RegisterAsync_DuplicateUsername_Rejected()
        {
            var db = CreateInMemoryDbContext();
            var keystroke = new KeystrokeService();
            var enc = CreateTestEncryptionService();
            var auth = new AuthService(db, keystroke, enc, NullLogger<AuthService>.Instance);

            var pwdSamples = new List<KeystrokeSample> { CreateSample(), CreateSample(), CreateSample(), CreateSample() };
            var actSamples = new List<KeystrokeSample> { CreateSample(), CreateSample(), CreateSample(), CreateSample() };

            await auth.RegisterAsync("bob_unique", "Password123!", "transfer phrase", pwdSamples, actSamples);
            var (success, msg, _) = await auth.RegisterAsync("bob_unique", "Password123!", "transfer phrase", pwdSamples, actSamples);

            Assert.False(success);
            Assert.Contains("already taken", msg);
        }

        [Fact]
        public async Task LoginAsync_TriggersLockout_AfterFiveFailedPasswordAttempts()
        {
            var db = CreateInMemoryDbContext();
            var keystroke = new KeystrokeService();
            var enc = CreateTestEncryptionService();
            var auth = new AuthService(db, keystroke, enc, NullLogger<AuthService>.Instance);

            var pwdSamples = new List<KeystrokeSample> { CreateSample(), CreateSample(), CreateSample(), CreateSample() };
            var actSamples = new List<KeystrokeSample> { CreateSample(), CreateSample(), CreateSample(), CreateSample() };

            await auth.RegisterAsync("charlie", "CorrectPassword123", "action phrase", pwdSamples, actSamples);

            // 4 failed attempts
            for (int i = 0; i < 4; i++)
            {
                var fail = await auth.LoginAsync("charlie", "WrongPassword", CreateSample());
                Assert.False(fail.IsSuccess);
                Assert.False(fail.IsLockedOut);
            }

            // 5th failed attempt should trigger lockout
            var fifthFail = await auth.LoginAsync("charlie", "WrongPassword", CreateSample());
            Assert.False(fifthFail.IsSuccess);
            Assert.True(fifthFail.IsLockedOut);
            Assert.Contains("Account locked", fifthFail.Message);

            // Attempting with correct password while locked out should still be rejected
            var attemptWhileLocked = await auth.LoginAsync("charlie", "CorrectPassword123", CreateSample());
            Assert.False(attemptWhileLocked.IsSuccess);
            Assert.True(attemptWhileLocked.IsLockedOut);
            Assert.Contains("locked", attemptWhileLocked.Message.ToLowerInvariant());
        }
    }
}
