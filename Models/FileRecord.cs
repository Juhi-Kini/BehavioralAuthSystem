using System;
using System.Collections.Generic;

namespace BehavioralAuthSystem.Models
{
    public class FileRecord
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string EncryptedPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string ContentType { get; set; } = "application/octet-stream";
        public string Tags { get; set; } = string.Empty;
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public User? User { get; set; }
        public ICollection<FileVersion> Versions { get; set; } = new List<FileVersion>();
        public ICollection<ShareLink> ShareLinks { get; set; } = new List<ShareLink>();
    }
}
