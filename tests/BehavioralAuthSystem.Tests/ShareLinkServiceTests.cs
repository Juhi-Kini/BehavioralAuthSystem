using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
    public class ShareLinkServiceTests
    {
        private class TestHostEnvironment : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = Environments.Development;
            public string ApplicationName { get; set; } = "BehavioralAuthSystem";
            public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "BehavioralAuthTests_" + Guid.NewGuid().ToString("N"));
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private static (AppDbContext, IFileStorageService, IShareLinkService) SetupServices()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

            var env = new TestHostEnvironment();
            Directory.CreateDirectory(Path.Combine(env.ContentRootPath, "App_Data", "uploads"));

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
                {
                    ["MasterEncryptionSecret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                })
                .Build();

            var enc = new EncryptionService(config, env, NullLogger<EncryptionService>.Instance);
            var fileStorage = new FileStorageService(db, enc, env, NullLogger<FileStorageService>.Instance);
            var shareLink = new ShareLinkService(db, fileStorage);

            return (db, fileStorage, shareLink);
        }

        [Fact]
        public async Task CreateShareLink_RejectsUnauthorizedFileId()
        {
            var (db, _, shareLink) = SetupServices();

            var user1 = new User { Id = 1, Username = "owner", EncryptionSalt = RandomNumberGenerator.GetBytes(32) };
            var user2 = new User { Id = 2, Username = "attacker", EncryptionSalt = RandomNumberGenerator.GetBytes(32) };
            db.Users.AddRange(user1, user2);

            var file = new FileRecord { Id = 10, UserId = 1, FileName = "secret.txt", EncryptedPath = "1/secret.enc" };
            db.Files.Add(file);
            await db.SaveChangesAsync();

            // Attacker (User 2) tries to create share link for User 1's file
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            {
                await shareLink.CreateShareLinkAsync(file.Id, user2.Id);
            });
        }

        [Fact]
        public async Task ValidateToken_RejectsExpiredLink()
        {
            var (db, _, shareLink) = SetupServices();

            var user = new User { Id = 1, Username = "owner", EncryptionSalt = RandomNumberGenerator.GetBytes(32) };
            var file = new FileRecord { Id = 1, UserId = 1, FileName = "doc.txt", EncryptedPath = "1/doc.enc" };
            db.Users.Add(user);
            db.Files.Add(file);
            await db.SaveChangesAsync();

            // Create link expired 1 hour ago
            var link = await shareLink.CreateShareLinkAsync(file.Id, user.Id, expiresAt: DateTime.UtcNow.AddHours(-1));

            var (isValid, msg, _, _) = await shareLink.ValidateTokenAsync(link.Token);
            Assert.False(isValid);
            Assert.Contains("expired", msg);
        }

        [Fact]
        public async Task ValidateToken_RejectsMaxAccessCountExceeded()
        {
            var (db, _, shareLink) = SetupServices();

            var user = new User { Id = 1, Username = "owner", EncryptionSalt = RandomNumberGenerator.GetBytes(32) };
            var file = new FileRecord { Id = 1, UserId = 1, FileName = "doc.txt", EncryptedPath = "1/doc.enc" };
            db.Users.Add(user);
            db.Files.Add(file);
            await db.SaveChangesAsync();

            // Link with max access count = 2, already accessed 2 times
            var link = await shareLink.CreateShareLinkAsync(file.Id, user.Id, maxAccessCount: 2);
            link.AccessCount = 2;
            await db.SaveChangesAsync();

            var (isValid, msg, _, _) = await shareLink.ValidateTokenAsync(link.Token);
            Assert.False(isValid);
            Assert.Contains("limit", msg);
        }

        [Fact]
        public async Task AccessShareLink_WithPasswordProtection_RequiresValidPassword()
        {
            var (db, fileStorage, shareLink) = SetupServices();

            var user = new User { Id = 1, Username = "owner", EncryptionSalt = RandomNumberGenerator.GetBytes(32) };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            byte[] plaintext = Encoding.UTF8.GetBytes("Protected content.");
            var (upSuccess, _, file) = await fileStorage.UploadFileAsync(user, "secure.txt", "text/plain", plaintext);
            Assert.True(upSuccess);
            Assert.NotNull(file);

            var link = await shareLink.CreateShareLinkAsync(file.Id, user.Id, password: "SecretLinkPassword!");

            // Attempt with wrong password
            var (wrongSuccess, wrongMsg, _, _, _) = await shareLink.AccessShareLinkAsync(link.Token, "WrongPass");
            Assert.False(wrongSuccess);
            Assert.Contains("Incorrect password", wrongMsg);

            // Attempt with correct password
            var (rightSuccess, _, downloaded, _, _) = await shareLink.AccessShareLinkAsync(link.Token, "SecretLinkPassword!");
            Assert.True(rightSuccess);
            Assert.NotNull(downloaded);
            Assert.Equal("Protected content.", Encoding.UTF8.GetString(downloaded));
        }

        [Fact]
        public async Task RevokeShareLink_OnlyOwnerCanRevoke()
        {
            var (db, _, shareLink) = SetupServices();

            var user = new User { Id = 1, Username = "owner", EncryptionSalt = RandomNumberGenerator.GetBytes(32) };
            var file = new FileRecord { Id = 1, UserId = 1, FileName = "doc.txt", EncryptedPath = "1/doc.enc" };
            db.Users.Add(user);
            db.Files.Add(file);
            await db.SaveChangesAsync();

            var link = await shareLink.CreateShareLinkAsync(file.Id, user.Id);

            // Other user (Id = 99) tries to revoke
            bool failRevoke = await shareLink.RevokeShareLinkAsync(link.Id, userId: 99);
            Assert.False(failRevoke);

            // Owner revokes
            bool ownerRevoke = await shareLink.RevokeShareLinkAsync(link.Id, userId: user.Id);
            Assert.True(ownerRevoke);

            // Link is now revoked and cannot be validated
            var (isValid, _, _, _) = await shareLink.ValidateTokenAsync(link.Token);
            Assert.False(isValid);
        }
    }
}
