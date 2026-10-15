using System;

namespace BehavioralAuthSystem.Models
{
    public class ShareLink
    {
        public int Id { get; set; }
        public int FileId { get; set; }
        public string Token { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }
        public int? MaxAccessCount { get; set; }
        public int AccessCount { get; set; } = 0;
        public string? SharePasswordHash { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public FileRecord? FileRecord { get; set; }
    }
}
