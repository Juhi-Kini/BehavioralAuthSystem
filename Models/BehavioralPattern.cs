using System;

namespace BehavioralAuthSystem.Models
{
    public class BehavioralPattern
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string ActionType { get; set; } = "login"; // "login", "Create", "Upload", "Update", or "Delete"
        public string PatternType { get; set; } = "keystroke-timing";
        
        // JSON containing the captured behavioral pattern data
        public string PatternData { get; set; } = string.Empty; 
        
        public double MeanHoldTime { get; set; }
        public double MeanLatency { get; set; }
        public double StdDevHoldTime { get; set; }
        public double StdDevLatency { get; set; }
        public double EnrollmentCV { get; set; } // Coefficient of Variation during enrollment
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public User? User { get; set; }
    }
}
