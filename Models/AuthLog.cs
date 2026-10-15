using System;

namespace BehavioralAuthSystem.Models
{
    public class AuthLog
    {
        public int Id { get; set; }
        public int? UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public bool IsSuccessful { get; set; }
        public double Score { get; set; } // 0 - 100 confidence
        public double ZScore { get; set; } // Raw mean Z-score
        public double AdaptiveThreshold { get; set; } // Threshold evaluated for this attempt
        public DateTime AttemptTime { get; set; } = DateTime.UtcNow;
        public bool IsSuspicious { get; set; } // Flagged by anomaly detection
        public string ActionType { get; set; } = "login"; // "login", "action_verify", "continuous", or "attack_simulation"
        public string FailureReason { get; set; } = string.Empty;
        public string? EvaluationLabel { get; set; }
        public User? User { get; set; }
    }
}
