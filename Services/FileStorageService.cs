using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BehavioralAuthSystem.Data;
using BehavioralAuthSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BehavioralAuthSystem.Services
{
    public interface IFileStorageService
    {
        Task<(bool IsSuccess, string Message, FileRecord? FileRecord)> UploadFileAsync(
            User user, string fileName, string contentType, byte[] plaintextBytes);

        Task<(bool IsSuccess, string Message, byte[]? PlaintextBytes, string FileName, string ContentType)> DownloadFileAsync(
            User user, int fileId);

        Task<byte[]?> DecryptFileContentAsync(byte[] userSalt, string encryptedFilePath);

        Task<List<FileRecord>> GetUserFilesAsync(int userId, bool includeDeleted = false);
        Task<FileRecord?> GetFileByIdAsync(int fileId, int userId);
        Task<long> GetUserStorageUsageAsync(int userId);
        Task<bool> SetFileTagsAsync(int fileId, int userId, string tags);
        
        Task<bool> SoftDeleteFileAsync(int fileId, int userId);
        Task<bool> RestoreFileAsync(int fileId, int userId);
        Task<bool> PermanentDeleteFileAsync(int fileId, int userId);

        Task<(bool IsSuccess, string Message, FileVersion? Version)> CreateVersionSnapshotAsync(
            User user, int fileId, byte[] newPlaintextBytes);

        Task<List<FileVersion>> GetFileVersionsAsync(int fileId, int userId);
        Task<(bool IsSuccess, string Message)> RestoreVersionAsync(int fileId, int versionId, int userId);
    }

    public class FileStorageService : IFileStorageService
    {
        private readonly AppDbContext _dbContext;
        private readonly IEncryptionService _encryptionService;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<FileStorageService> _logger;

                private const long MaxSingleFileSize = 50 * 1024 * 1024; // 50 MB per file safeguard
        private readonly string _uploadBaseDir;

        public FileStorageService(
            AppDbContext dbContext,
            IEncryptionService encryptionService,
            IHostEnvironment environment,
            ILogger<FileStorageService> logger)
        {
            _dbContext = dbContext;
            _encryptionService = encryptionService;
            _environment = environment;
            _logger = logger;

            _uploadBaseDir = Path.Combine(_environment.ContentRootPath, "App_Data", "uploads");
            if (!Directory.Exists(_uploadBaseDir))
            {
                Directory.CreateDirectory(_uploadBaseDir);
            }
        }

        public async Task<long> GetUserStorageUsageAsync(int userId)
        {
            return await _dbContext.Files
                .Where(f => f.UserId == userId && !f.IsDeleted)
                .SumAsync(f => (long?)f.SizeBytes) ?? 0L;
        }

        public async Task<(bool IsSuccess, string Message, FileRecord? FileRecord)> UploadFileAsync(
            User user, string fileName, string contentType, byte[] plaintextBytes)
        {
            if (user == null) return (false, "User required.", null);
            if (plaintextBytes == null || plaintextBytes.Length == 0) return (false, "File cannot be empty.", null);

            if (plaintextBytes.Length > MaxSingleFileSize)
            {
                return (false, "File exceeds maximum upload limit of 50 MB.", null);
            }

            // Sanitize filename to prevent directory traversal
            string safeFileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                safeFileName = $"file_{DateTime.UtcNow:yyyyMMddHHmmss}.bin";
            }

            long currentUsage = await GetUserStorageUsageAsync(user.Id);
            var quota = user.StorageQuotaBytes > 0 ? user.StorageQuotaBytes : 500L * 1024 * 1024;
            if (currentUsage + plaintextBytes.Length > quota)
            {
                return (false, $"Upload failed: Storage quota exceeded. Limit: {quota / (1024.0 * 1024.0):F0} MB. Current usage: {currentUsage / (1024.0 * 1024.0):F2} MB.", null);
            }

            // Encrypt using AES-256-GCM
            byte[] encryptedBytes = _encryptionService.EncryptData(user.EncryptionSalt, plaintextBytes);

            string userFolder = Path.Combine(_uploadBaseDir, user.Id.ToString());
            if (!Directory.Exists(userFolder))
            {
                Directory.CreateDirectory(userFolder);
            }

            string relativeEncryptedPath = Path.Combine(user.Id.ToString(), $"{Guid.NewGuid():N}.enc");
            string fullEncryptedPath = Path.Combine(_uploadBaseDir, relativeEncryptedPath);

            await File.WriteAllBytesAsync(fullEncryptedPath, encryptedBytes);

            var fileRecord = new FileRecord
            {
                UserId = user.Id,
                FileName = safeFileName,
                EncryptedPath = relativeEncryptedPath,
                SizeBytes = plaintextBytes.Length,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Files.Add(fileRecord);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("File {FileName} (ID: {FileId}) uploaded and encrypted for User {UserId}.", safeFileName, fileRecord.Id, user.Id);

            return (true, "File uploaded and encrypted successfully.", fileRecord);
        }

        public async Task<(bool IsSuccess, string Message, byte[]? PlaintextBytes, string FileName, string ContentType)> DownloadFileAsync(
            User user, int fileId)
        {
            if (user == null) return (false, "User required.", null, string.Empty, string.Empty);

            var fileRecord = await GetFileByIdAsync(fileId, user.Id);
            if (fileRecord == null || fileRecord.IsDeleted)
            {
                return (false, "File not found or deleted.", null, string.Empty, string.Empty);
            }

            string fullPath = Path.Combine(_uploadBaseDir, fileRecord.EncryptedPath);
            if (!File.Exists(fullPath))
            {
                return (false, "Encrypted file missing from storage disk.", null, string.Empty, string.Empty);
            }

            byte[] encryptedData = await File.ReadAllBytesAsync(fullPath);
            byte[] decryptedBytes = _encryptionService.DecryptData(user.EncryptionSalt, encryptedData);

            return (true, "Success", decryptedBytes, fileRecord.FileName, fileRecord.ContentType);
        }

        public async Task<byte[]?> DecryptFileContentAsync(byte[] userSalt, string encryptedFilePath)
        {
            string fullPath = Path.Combine(_uploadBaseDir, encryptedFilePath);
            if (!File.Exists(fullPath)) return null;

            byte[] encryptedData = await File.ReadAllBytesAsync(fullPath);
            return _encryptionService.DecryptData(userSalt, encryptedData);
        }

        public async Task<List<FileRecord>> GetUserFilesAsync(int userId, bool includeDeleted = false)
        {
            return await _dbContext.Files
                .Where(f => f.UserId == userId && (includeDeleted || !f.IsDeleted))
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();
        }

        public async Task<FileRecord?> GetFileByIdAsync(int fileId, int userId)
        {
            return await _dbContext.Files
                .FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId);
        }


        public async Task<bool> SetFileTagsAsync(int fileId, int userId, string tags)
        {
            var file = await GetFileByIdAsync(fileId, userId);
            if (file == null) return false;
            file.Tags = string.Join(",", (tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).Take(20));
            await _dbContext.SaveChangesAsync();
            return true;
        }
        public async Task<bool> SoftDeleteFileAsync(int fileId, int userId)
        {
            var file = await GetFileByIdAsync(fileId, userId);
            if (file == null) return false;

            file.IsDeleted = true;
            file.DeletedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RestoreFileAsync(int fileId, int userId)
        {
            var file = await _dbContext.Files.FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId);
            if (file == null) return false;

            file.IsDeleted = false;
            file.DeletedAt = null;
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> PermanentDeleteFileAsync(int fileId, int userId)
        {
            var file = await _dbContext.Files
                .Include(f => f.Versions)
                .Include(f => f.ShareLinks)
                .FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId);

            if (file == null) return false;

            // Delete active physical encrypted file
            string fullPath = Path.Combine(_uploadBaseDir, file.EncryptedPath);
            if (File.Exists(fullPath))
            {
                try { File.Delete(fullPath); } catch { }
            }

            // Delete version physical files
            foreach (var ver in file.Versions)
            {
                string verPath = Path.Combine(_uploadBaseDir, ver.VersionData);
                if (File.Exists(verPath))
                {
                    try { File.Delete(verPath); } catch { }
                }
            }

            _dbContext.Files.Remove(file);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<(bool IsSuccess, string Message, FileVersion? Version)> CreateVersionSnapshotAsync(
            User user, int fileId, byte[] newPlaintextBytes)
        {
            var fileRecord = await GetFileByIdAsync(fileId, user.Id);
            if (fileRecord == null) return (false, "File not found.", null);

            var currentUsage = await GetUserStorageUsageAsync(user.Id);
            var quota = user.StorageQuotaBytes > 0 ? user.StorageQuotaBytes : 500L * 1024 * 1024;
            var projectedUsage = currentUsage - fileRecord.SizeBytes + newPlaintextBytes.Length;
            if (projectedUsage > quota)
                return (false, $"Update would exceed your storage quota of {quota / (1024.0 * 1024.0):F0} MB.", null);

            string versionsDir = Path.Combine(_uploadBaseDir, user.Id.ToString(), "versions");
            if (!Directory.Exists(versionsDir))
            {
                Directory.CreateDirectory(versionsDir);
            }

            // Snapshot existing file state
            string existingFullPath = Path.Combine(_uploadBaseDir, fileRecord.EncryptedPath);
            int nextVerNumber = (await _dbContext.FileVersions.Where(v => v.FileId == fileId).CountAsync()) + 1;

            string relativeVerPath = Path.Combine(user.Id.ToString(), "versions", $"{fileId}_v{nextVerNumber}_{Guid.NewGuid():N}.enc");
            string fullVerPath = Path.Combine(_uploadBaseDir, relativeVerPath);

            if (File.Exists(existingFullPath))
            {
                File.Copy(existingFullPath, fullVerPath, overwrite: true);
            }

            var versionEntity = new FileVersion
            {
                FileId = fileId,
                VersionNumber = nextVerNumber,
                VersionData = relativeVerPath,
                SizeBytes = fileRecord.SizeBytes,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.FileVersions.Add(versionEntity);

            // Re-encrypt and overwrite current active file
            byte[] newEncryptedBytes = _encryptionService.EncryptData(user.EncryptionSalt, newPlaintextBytes);
            await File.WriteAllBytesAsync(existingFullPath, newEncryptedBytes);

            fileRecord.SizeBytes = newPlaintextBytes.Length;
            await _dbContext.SaveChangesAsync();

            return (true, $"Snapshot v{nextVerNumber} created and file updated.", versionEntity);
        }

        public async Task<List<FileVersion>> GetFileVersionsAsync(int fileId, int userId)
        {
            var fileExists = await _dbContext.Files.AnyAsync(f => f.Id == fileId && f.UserId == userId);
            if (!fileExists) return new List<FileVersion>();

            return await _dbContext.FileVersions
                .Where(v => v.FileId == fileId)
                .OrderByDescending(v => v.VersionNumber)
                .ToListAsync();
        }

        public async Task<(bool IsSuccess, string Message)> RestoreVersionAsync(int fileId, int versionId, int userId)
        {
            var file = await GetFileByIdAsync(fileId, userId);
            if (file == null || file.IsDeleted)
            {
                return (false, "File not found or deleted.");
            }

            var version = await _dbContext.FileVersions
                .FirstOrDefaultAsync(v => v.Id == versionId && v.FileId == fileId);

            if (version == null)
            {
                return (false, "Version snapshot not found.");
            }

            string verFullPath = Path.Combine(_uploadBaseDir, version.VersionData);
            if (!File.Exists(verFullPath))
            {
                return (false, "Version snapshot physical file missing from disk.");
            }

            string activeFullPath = Path.Combine(_uploadBaseDir, file.EncryptedPath);

            // Before restoring, create a snapshot of current state so user doesn't lose current work
            int nextVer = (await _dbContext.FileVersions.Where(v => v.FileId == fileId).CountAsync()) + 1;
            string backupRelPath = Path.Combine(userId.ToString(), "versions", $"{fileId}_v{nextVer}_pre_restore_{Guid.NewGuid():N}.enc");
            string backupFullPath = Path.Combine(_uploadBaseDir, backupRelPath);

            if (File.Exists(activeFullPath))
            {
                File.Copy(activeFullPath, backupFullPath, overwrite: true);
                _dbContext.FileVersions.Add(new FileVersion
                {
                    FileId = fileId,
                    VersionNumber = nextVer,
                    VersionData = backupRelPath,
                    SizeBytes = file.SizeBytes,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Restore version file
            File.Copy(verFullPath, activeFullPath, overwrite: true);
            file.SizeBytes = version.SizeBytes;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("File {FileId} restored to version {VersionNumber} by user {UserId}.", fileId, version.VersionNumber, userId);
            return (true, $"Restored file to version v{version.VersionNumber}.");
        }
    }
}
