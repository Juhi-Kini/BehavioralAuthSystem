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
    public class FileStorageServiceTests
    {
        private class TestHostEnvironment : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = Environments.Development;
            public string ApplicationName { get; set; } = "BehavioralAuthSystem";
            public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "FileStorageTests_" + Guid.NewGuid().ToString("N"));
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private static (AppDbContext, IFileStorageService, TestHostEnvironment) SetupServices()
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

            return (db, fileStorage, env);
        }

        [Fact]
        public async Task Upload_StoresEncryptedBytesOnDisk_AndDownloadRestoresPlaintext()
        {
            var (db, fileStorage, env) = SetupServices();

            var user = new User { Id = 1, Username = "alice", EncryptionSalt = RandomNumberGenerator.GetBytes(32) };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            byte[] plaintext = Encoding.UTF8.GetBytes("Encrypted content inside vault.");
            var (upSuccess, _, file) = await fileStorage.UploadFileAsync(user, "test.txt", "text/plain", plaintext);

            Assert.True(upSuccess);
            Assert.NotNull(file);

            // Confirm disk storage is NOT plaintext
            string diskPath = Path.Combine(env.ContentRootPath, "App_Data", "uploads", file.EncryptedPath);
            Assert.True(File.Exists(diskPath));
            byte[] diskBytes = await File.ReadAllBytesAsync(diskPath);
            Assert.NotEqual(plaintext, diskBytes);

            // Download with authorized user decrypts cleanly
            var (dlSuccess, _, downloaded, name, type) = await fileStorage.DownloadFileAsync(user, file.Id);
            Assert.True(dlSuccess);
            Assert.Equal("test.txt", name);
            Assert.Equal(plaintext, downloaded);
        }

        [Fact]
        public async Task DownloadFile_EnforcesUserIsolation_CrossUserAccessForbidden()
        {
            var (db, fileStorage, _) = SetupServices();

            var user1 = new User { Id = 1, Username = "alice", EncryptionSalt = RandomNumberGenerator.GetBytes(32) };
            var user2 = new User { Id = 2, Username = "mallory", EncryptionSalt = RandomNumberGenerator.GetBytes(32) };
            db.Users.AddRange(user1, user2);
            await db.SaveChangesAsync();

            byte[] plaintext = Encoding.UTF8.GetBytes("Alice's private secret.");
            var (upSuccess, _, file) = await fileStorage.UploadFileAsync(user1, "alice.txt", "text/plain", plaintext);
            Assert.True(upSuccess);
            Assert.NotNull(file);

            // Mallory (User 2) attempts to download Alice's (User 1) file
            var (dlSuccess, dlMsg, downloaded, _, _) = await fileStorage.DownloadFileAsync(user2, file.Id);

            Assert.False(dlSuccess);
            Assert.Null(downloaded);
            Assert.Contains("not found", dlMsg.ToLowerInvariant());
        }

        [Fact]
        public async Task SoftDelete_And_Restore_Lifecycle()
        {
            var (db, fileStorage, _) = SetupServices();

            var user = new User { Id = 1, Username = "alice", EncryptionSalt = RandomNumberGenerator.GetBytes(32) };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var (_, _, file) = await fileStorage.UploadFileAsync(user, "doc.txt", "text/plain", Encoding.UTF8.GetBytes("content"));
            Assert.NotNull(file);

            // Soft delete
            bool deleteOk = await fileStorage.SoftDeleteFileAsync(file.Id, user.Id);
            Assert.True(deleteOk);

            var activeFiles = await fileStorage.GetUserFilesAsync(user.Id, includeDeleted: false);
            Assert.DoesNotContain(activeFiles, f => f.Id == file.Id);

            var deletedFiles = await fileStorage.GetUserFilesAsync(user.Id, includeDeleted: true);
            Assert.Contains(deletedFiles, f => f.Id == file.Id && f.IsDeleted);

            // Restore
            bool restoreOk = await fileStorage.RestoreFileAsync(file.Id, user.Id);
            Assert.True(restoreOk);

            var restoredFiles = await fileStorage.GetUserFilesAsync(user.Id, includeDeleted: false);
            Assert.Contains(restoredFiles, f => f.Id == file.Id && !f.IsDeleted);
        }

        [Fact]
        public async Task VersionSnapshot_And_Restore_Functionality()
        {
            var (db, fileStorage, _) = SetupServices();

            var user = new User { Id = 1, Username = "alice", EncryptionSalt = RandomNumberGenerator.GetBytes(32) };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            // Initial version v1
            var (_, _, file) = await fileStorage.UploadFileAsync(user, "notes.md", "text/markdown", Encoding.UTF8.GetBytes("Version 1 content"));
            Assert.NotNull(file);

            // Save new version v2 (creates snapshot of v1)
            var (snapSuccess, _, version1) = await fileStorage.CreateVersionSnapshotAsync(user, file.Id, Encoding.UTF8.GetBytes("Version 2 content"));
            Assert.True(snapSuccess);
            Assert.NotNull(version1);
            Assert.Equal(1, version1.VersionNumber);

            // Check current active content is Version 2
            var (_, _, v2Bytes, _, _) = await fileStorage.DownloadFileAsync(user, file.Id);
            Assert.Equal("Version 2 content", Encoding.UTF8.GetString(v2Bytes!));

            // Restore version 1
            var (restoreSuccess, restoreMsg) = await fileStorage.RestoreVersionAsync(file.Id, version1.Id, user.Id);
            Assert.True(restoreSuccess);

            // Confirm active file is now reverted to Version 1 content
            var (_, _, restoredBytes, _, _) = await fileStorage.DownloadFileAsync(user, file.Id);
            Assert.Equal("Version 1 content", Encoding.UTF8.GetString(restoredBytes!));
        }

        [Fact]
        public async Task PermanentDelete_RemovesDbRecordAndPhysicalFile()
        {
            var (db, fileStorage, env) = SetupServices();

            var user = new User { Id = 1, Username = "alice", EncryptionSalt = RandomNumberGenerator.GetBytes(32) };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var (_, _, file) = await fileStorage.UploadFileAsync(user, "purge_test.txt", "text/plain", Encoding.UTF8.GetBytes("purge me"));
            Assert.NotNull(file);

            string diskPath = Path.Combine(env.ContentRootPath, "App_Data", "uploads", file.EncryptedPath);
            Assert.True(File.Exists(diskPath));

            bool purged = await fileStorage.PermanentDeleteFileAsync(file.Id, user.Id);
            Assert.True(purged);

            // Physical file must be deleted from disk
            Assert.False(File.Exists(diskPath));

            // Record removed from DB
            var dbRecord = await db.Files.FirstOrDefaultAsync(f => f.Id == file.Id);
            Assert.Null(dbRecord);
        }
    }
}
