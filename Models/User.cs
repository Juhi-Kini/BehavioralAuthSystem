using System;
using System.Collections.Generic;

namespace BehavioralAuthSystem.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? Email { get; set; }
        public DateTime? EmailVerifiedAt { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public string ActionPhraseHash { get; set; } = string.Empty;
        public byte[] EncryptionSalt { get; set; } = Array.Empty<byte>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockoutUntil { get; set; }
        public long StorageQuotaBytes { get; set; } = 500L * 1024 * 1024;
        public string? PasswordResetCodeHash { get; set; }
        public DateTime? PasswordResetExpiresAt { get; set; }

        public ICollection<BehavioralPattern> BehavioralPatterns { get; set; } = new List<BehavioralPattern>();
        public ICollection<AuthLog> AuthLogs { get; set; } = new List<AuthLog>();
        public ICollection<FileRecord> Files { get; set; } = new List<FileRecord>();
    }
}
