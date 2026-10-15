using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BehavioralAuthSystem.Data;
using BehavioralAuthSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace BehavioralAuthSystem.Services
{
    public interface IShareLinkService
    {
        Task<ShareLink> CreateShareLinkAsync(
            int fileId, int userId, DateTime? expiresAt = null, int? maxAccessCount = null, string? password = null);

        Task<(bool IsValid, string Message, ShareLink? Link, FileRecord? FileRecord)> ValidateTokenAsync(string token);

        Task<(bool IsSuccess, string Message, byte[]? PlaintextBytes, string FileName, string ContentType)> AccessShareLinkAsync(
            string token, string? password = null);

        Task<bool> RevokeShareLinkAsync(int shareLinkId, int userId);
        Task<List<ShareLink>> GetShareLinksForFileAsync(int fileId, int userId);
    }

    public class ShareLinkService : IShareLinkService
    {
        private readonly AppDbContext _dbContext;
        private readonly IFileStorageService _fileStorageService;

        public ShareLinkService(AppDbContext dbContext, IFileStorageService fileStorageService)
        {
            _dbContext = dbContext;
            _fileStorageService = fileStorageService;
        }

        public async Task<ShareLink> CreateShareLinkAsync(
            int fileId, int userId, DateTime? expiresAt = null, int? maxAccessCount = null, string? password = null)
        {
            // Verify file ownership and ensure file is not soft-deleted
            var file = await _dbContext.Files.FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId && !f.IsDeleted);
            if (file == null)
            {
                throw new UnauthorizedAccessException("Cannot create a share link: File not found or not owned by user.");
            }

            // Cryptographically secure 128-bit unguessable random token (32 hex characters)
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            string? pwdHash = !string.IsNullOrWhiteSpace(password) ? HashPassword(password) : null;

            var link = new ShareLink
            {
                FileId = fileId,
                Token = token,
                ExpiresAt = expiresAt,
                MaxAccessCount = maxAccessCount,
                AccessCount = 0,
                SharePasswordHash = pwdHash,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.ShareLinks.Add(link);
            await _dbContext.SaveChangesAsync();

            return link;
        }

        public async Task<List<ShareLink>> GetShareLinksForFileAsync(int fileId, int userId)
        {
            var file = await _dbContext.Files.FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId);
            if (file == null) return new List<ShareLink>();

            return await _dbContext.ShareLinks
                .Where(sl => sl.FileId == fileId)
                .OrderByDescending(sl => sl.CreatedAt)
                .ToListAsync();
        }

        public async Task<(bool IsValid, string Message, ShareLink? Link, FileRecord? FileRecord)> ValidateTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return (false, "Invalid share token.", null, null);
            }

            var link = await _dbContext.ShareLinks
                .Include(sl => sl.FileRecord)
                    .ThenInclude(f => f!.User)
                .FirstOrDefaultAsync(sl => sl.Token == token);

            if (link == null || link.FileRecord == null || link.FileRecord.IsDeleted)
            {
                return (false, "Invalid or expired share link.", null, null);
            }

            if (link.ExpiresAt.HasValue && link.ExpiresAt.Value < DateTime.UtcNow)
            {
                return (false, "This share link has expired.", link, link.FileRecord);
            }

            if (link.MaxAccessCount.HasValue && link.AccessCount >= link.MaxAccessCount.Value)
            {
                return (false, "Maximum download limit for this link has been reached.", link, link.FileRecord);
            }

            return (true, "Valid link", link, link.FileRecord);
        }

        public async Task<(bool IsSuccess, string Message, byte[]? PlaintextBytes, string FileName, string ContentType)>
    AccessShareLinkAsync(string token, string? password = null)
        {
            var (isValid, validationMsg, link, fileRecord) =
                await ValidateTokenAsync(token);

            if (!isValid || link == null || fileRecord == null ||
                fileRecord.User == null)
            {
                return (false, validationMsg, null, string.Empty, string.Empty);
            }

            if (!string.IsNullOrEmpty(link.SharePasswordHash))
            {
                if (string.IsNullOrEmpty(password) ||
                    !VerifyPassword(password, link.SharePasswordHash))
                {
                    return (
                        false,
                        "Incorrect passcode for this share link.",
                        null,
                        fileRecord.FileName,
                        fileRecord.ContentType);
                }
            }

            try
            {
                byte[]? plaintext =
                    await _fileStorageService.DecryptFileContentAsync(
                        fileRecord.User.EncryptionSalt,
                        fileRecord.EncryptedPath);

                if (plaintext == null)
                {
                    return (
                        false,
                        "Encrypted file not found. Check the stored file path.",
                        null,
                        fileRecord.FileName,
                        fileRecord.ContentType);
                }

                link.AccessCount++;
                await _dbContext.SaveChangesAsync();

                return (
                    true,
                    "Success",
                    plaintext,
                    fileRecord.FileName,
                    fileRecord.ContentType);
            }
            catch (CryptographicException)
            {
                return (
                    false,
                    "File decryption failed. The encryption key or encrypted file may not match.",
                    null,
                    fileRecord.FileName,
                    fileRecord.ContentType);
            }
            catch (IOException)
            {
                return (
                    false,
                    "The encrypted file could not be read from storage.",
                    null,
                    fileRecord.FileName,
                    fileRecord.ContentType);
            }
        }

        public async Task<bool> RevokeShareLinkAsync(int shareLinkId, int userId)
        {
            var link = await _dbContext.ShareLinks
                .Include(sl => sl.FileRecord)
                .FirstOrDefaultAsync(sl => sl.Id == shareLinkId && sl.FileRecord != null && sl.FileRecord.UserId == userId);

            if (link == null) return false;

            _dbContext.ShareLinks.Remove(link);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        private static string HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256))
            {
                byte[] hash = pbkdf2.GetBytes(32);
                byte[] combined = new byte[48];
                Buffer.BlockCopy(salt, 0, combined, 0, 16);
                Buffer.BlockCopy(hash, 0, combined, 16, 32);
                return Convert.ToBase64String(combined);
            }
        }

        private static bool VerifyPassword(string password, string storedHash)
        {
            try
            {
                byte[] combined = Convert.FromBase64String(storedHash);
                if (combined.Length != 48) return false;

                byte[] salt = new byte[16];
                Buffer.BlockCopy(combined, 0, salt, 0, 16);

                using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256))
                {
                    byte[] hash = pbkdf2.GetBytes(32);
                    return CryptographicOperations.FixedTimeEquals(combined.AsSpan(16, 32), hash);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
