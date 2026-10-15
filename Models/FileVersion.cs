using System;

namespace BehavioralAuthSystem.Models
{
    public class FileVersion
    {
        public int Id { get; set; }
        public int FileId { get; set; }
        public int VersionNumber { get; set; }
        public string VersionData { get; set; } = string.Empty; // Encrypted path to snapshot
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public FileRecord? FileRecord { get; set; }
    }
}
