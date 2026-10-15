using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BehavioralAuthSystem.Models;

namespace BehavioralAuthSystem.Services
{
    public class KeystrokeSample
    {
        public List<double> HoldTimes { get; set; } = new List<double>();
        public List<double> Latencies { get; set; } = new List<double>();
        public double TotalDuration { get; set; }
        public int KeyCount { get; set; }
    }

    public class PatternDataPayload
    {
        public List<double> HoldMeans { get; set; } = new List<double>();
        public List<double> HoldStdDevs { get; set; } = new List<double>();
        public List<double> LatencyMeans { get; set; } = new List<double>();
        public List<double> LatencyStdDevs { get; set; } = new List<double>();
    }

    public class VerificationResult
    {
        public bool IsPassed { get; set; }
        public double ZScore { get; set; }
        public double ConfidenceScore { get; set; } // 0 - 100%
        public double AdaptiveThresholdScore { get; set; } // Pass cutoff confidence (100 / e ≈ 36.8%)
        public double AdaptiveZThreshold { get; set; } // Adaptive distance threshold (1.8 + 1.2 * CV)
        public bool IsAnomalous { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public interface IKeystrokeService
    {
        (bool IsValid, string Message, BehavioralPattern? Pattern, double EnrollmentCV) ProcessEnrollment(
            int userId, string actionType, List<KeystrokeSample> samples);

        VerificationResult VerifySample(KeystrokeSample sample, BehavioralPattern pattern, List<AuthLog>? historicalLogs = null);
    }

    public class KeystrokeService : IKeystrokeService
    {
        public const double BaseZThreshold = 1.8;
        public const double CVMultiplier = 1.2;
        public const double MinimumStdDevHold = 5.0; // ms floor to avoid division by near-zero
        public const double MinimumStdDevLatency = 8.0; // ms floor to avoid division by near-zero

        public (bool IsValid, string Message, BehavioralPattern? Pattern, double EnrollmentCV) ProcessEnrollment(
            int userId, string actionType, List<KeystrokeSample> samples)
        {
            if (samples == null || samples.Count != 4)
            {
                return (false, "Enrollment requires exactly 4 timing samples.", null, 1.0);
            }

            // Validate that none of the samples are null
            if (samples.Any(s => s == null || s.HoldTimes == null || s.Latencies == null))
            {
                return (false, "Malformed enrollment sample data received.", null, 1.0);
            }

            int keyCount = samples[0].HoldTimes.Count;
            int latCount = samples[0].Latencies.Count;

            if (keyCount < 3)
            {
                return (false, "Enrollment sequence too short: at least 3 keystrokes required.", null, 1.0);
            }

            // Ensure exact sequence length match across all 4 samples (no truncated or extra keystrokes)
            if (samples.Any(s => s.HoldTimes.Count != keyCount || s.Latencies.Count != latCount))
            {
                return (false, "Key count mismatch across enrollment attempts. Please type cleanly without backspacing.", null, 1.0);
            }

            // Validate timing values: all must be finite, positive, and physically plausible (< 15 seconds per key)
            foreach (var sample in samples)
            {
                if (sample.HoldTimes.Any(t => !double.IsFinite(t) || t <= 0.0 || t > 15000.0) ||
                    sample.Latencies.Any(t => !double.IsFinite(t) || t <= 0.0 || t > 15000.0))
                {
                    return (false, "Invalid or out-of-range timing measurement detected. Please type with a steady rhythm.", null, 1.0);
                }
            }

            var holdMeans = new List<double>();
            var holdStdDevs = new List<double>();
            var latencyMeans = new List<double>();
            var latencyStdDevs = new List<double>();
            var cvValues = new List<double>();

            // Process Hold Times across all 4 samples
            for (int i = 0; i < keyCount; i++)
            {
                var values = samples.Select(s => s.HoldTimes[i]).ToList();
                double mean = values.Average();
                double sumSqDiff = values.Sum(v => Math.Pow(v - mean, 2));
                double stdDev = Math.Sqrt(sumSqDiff / values.Count);
                
                holdMeans.Add(mean);
                holdStdDevs.Add(Math.Max(stdDev, MinimumStdDevHold));

                if (mean > 0)
                {
                    cvValues.Add(stdDev / mean);
                }
            }

            // Process Latencies across all 4 samples
            for (int i = 0; i < latCount; i++)
            {
                var values = samples.Select(s => s.Latencies[i]).ToList();
                double mean = values.Average();
                double sumSqDiff = values.Sum(v => Math.Pow(v - mean, 2));
                double stdDev = Math.Sqrt(sumSqDiff / values.Count);

                latencyMeans.Add(mean);
                latencyStdDevs.Add(Math.Max(stdDev, MinimumStdDevLatency));

                if (mean > 0)
                {
                    cvValues.Add(stdDev / mean);
                }
            }

            double overallCV = cvValues.Count > 0 ? cvValues.Average() : 0.0;

            // Strict quality check: Reject if Coefficient of Variation > 0.40
            if (overallCV > 0.40)
            {
                return (false, $"Enrollment rejected: Typing rhythm was too inconsistent (CV = {overallCV:F2} > 0.40). Please re-type with a natural, steady rhythm.", null, overallCV);
            }

            var payload = new PatternDataPayload
            {
                HoldMeans = holdMeans,
                HoldStdDevs = holdStdDevs,
                LatencyMeans = latencyMeans,
                LatencyStdDevs = latencyStdDevs
            };

            double meanHoldTotal = samples.SelectMany(s => s.HoldTimes).DefaultIfEmpty(0).Average();
            double meanLatTotal = samples.SelectMany(s => s.Latencies).DefaultIfEmpty(0).Average();
            double stdHoldTotal = Math.Sqrt(samples.SelectMany(s => s.HoldTimes).Select(h => Math.Pow(h - meanHoldTotal, 2)).DefaultIfEmpty(0).Average());
            double stdLatTotal = Math.Sqrt(samples.SelectMany(s => s.Latencies).Select(l => Math.Pow(l - meanLatTotal, 2)).DefaultIfEmpty(0).Average());

            var pattern = new BehavioralPattern
            {
                UserId = userId,
                ActionType = actionType,
                PatternType = "keystroke-timing",
                PatternData = JsonSerializer.Serialize(payload),
                MeanHoldTime = meanHoldTotal,
                MeanLatency = meanLatTotal,
                StdDevHoldTime = Math.Max(stdHoldTotal, MinimumStdDevHold),
                StdDevLatency = Math.Max(stdLatTotal, MinimumStdDevLatency),
                EnrollmentCV = overallCV,
                CreatedAt = DateTime.UtcNow
            };

            return (true, "Enrollment biometric template created successfully.", pattern, overallCV);
        }

        public VerificationResult VerifySample(KeystrokeSample sample, BehavioralPattern pattern, List<AuthLog>? historicalLogs = null)
        {
            double fallbackAdaptiveThreshold = pattern != null ? BaseZThreshold + (CVMultiplier * pattern.EnrollmentCV) : BaseZThreshold;
            double passCutoffConfidence = 100.0 / Math.E; // ≈ 36.7879%

            if (sample == null || pattern == null || string.IsNullOrWhiteSpace(pattern.PatternData))
            {
                return new VerificationResult
                {
                    IsPassed = false,
                    ZScore = 99.0,
                    ConfidenceScore = 0.0,
                    AdaptiveThresholdScore = passCutoffConfidence,
                    AdaptiveZThreshold = fallbackAdaptiveThreshold,
                    Message = "Missing sample or behavioral pattern template."
                };
            }

            PatternDataPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<PatternDataPayload>(pattern.PatternData);
            }
            catch
            {
                return new VerificationResult
                {
                    IsPassed = false,
                    ZScore = 99.0,
                    ConfidenceScore = 0.0,
                    AdaptiveThresholdScore = passCutoffConfidence,
                    AdaptiveZThreshold = fallbackAdaptiveThreshold,
                    Message = "Corrupted template data."
                };
            }

            if (payload == null || payload.HoldMeans == null || payload.LatencyMeans == null || payload.HoldMeans.Count < 3)
            {
                return new VerificationResult
                {
                    IsPassed = false,
                    ZScore = 99.0,
                    ConfidenceScore = 0.0,
                    AdaptiveThresholdScore = passCutoffConfidence,
                    AdaptiveZThreshold = fallbackAdaptiveThreshold,
                    Message = "Invalid template structure."
                };
            }

            // CRITICAL FIX: Reject mismatched, truncated, or empty timing sequences rather than comparing overlapping subsets!
            if (sample.HoldTimes == null ||
    sample.Latencies == null ||
    sample.HoldTimes.Count != payload.HoldMeans.Count ||
    sample.Latencies.Count != payload.LatencyMeans.Count)
            {
                return new VerificationResult
                {
                    IsPassed = false,
                    ZScore = 99.0,
                    ConfidenceScore = 0.0,
                    AdaptiveThresholdScore = passCutoffConfidence,
                    AdaptiveZThreshold = fallbackAdaptiveThreshold,
                    Message =
                        $"Biometric sequence mismatch. " +
                        $"Hold times: received {sample.HoldTimes?.Count ?? 0}, " +
                        $"enrolled {payload.HoldMeans.Count}. " +
                        $"Latencies: received {sample.Latencies?.Count ?? 0}, " +
                        $"enrolled {payload.LatencyMeans.Count}."
                };
            }

            // Validate all timing values are positive and finite
            if (sample.HoldTimes.Any(t => !double.IsFinite(t) || t <= 0.0) ||
                sample.Latencies.Any(t => !double.IsFinite(t) || t <= 0.0))
            {
                return new VerificationResult
                {
                    IsPassed = false,
                    ZScore = 99.0,
                    ConfidenceScore = 0.0,
                    AdaptiveThresholdScore = passCutoffConfidence,
                    AdaptiveZThreshold = fallbackAdaptiveThreshold,
                    Message = "Biometric verification failed: Invalid non-positive or non-finite timing values."
                };
            }

            int keyCount = payload.HoldMeans.Count;
            int latCount = payload.LatencyMeans.Count;

            var holdZScores = new List<double>();
            for (int i = 0; i < keyCount; i++)
            {
                double mean = payload.HoldMeans[i];
                double stdDev = Math.Max(payload.HoldStdDevs[i], MinimumStdDevHold);
                double z = Math.Abs(sample.HoldTimes[i] - mean) / stdDev;
                holdZScores.Add(z);
            }

            var latZScores = new List<double>();
            for (int i = 0; i < latCount; i++)
            {
                double mean = payload.LatencyMeans[i];
                double stdDev = Math.Max(payload.LatencyStdDevs[i], MinimumStdDevLatency);
                double z = Math.Abs(sample.Latencies[i] - mean) / stdDev;
                latZScores.Add(z);
            }

            // Combined weighted absolute distance D across hold times and latencies
            double totalZ = holdZScores.Sum() + latZScores.Sum();
            int totalCount = holdZScores.Count + latZScores.Count;
            double distance = totalCount > 0 ? totalZ / totalCount : 99.0;

            // Compute Adaptive Threshold: AdaptiveThreshold = 1.8 + 1.2 * EnrollmentCV
            double adaptiveThreshold = BaseZThreshold + (CVMultiplier * pattern.EnrollmentCV);

            // Confidence calculation: Confidence = 100 * exp(-Distance / AdaptiveThreshold)
            double confidenceScore = Math.Max(0.0, Math.Min(100.0, 100.0 * Math.Exp(-distance / adaptiveThreshold)));

            // Pass boundary: Confidence >= 100 / e ≈ 36.7879% (which occurs when distance <= adaptiveThreshold)
            bool isPassed = confidenceScore >= passCutoffConfidence;

            // Historical Anomaly Detection: Z-score comparison against past successful login scores
            bool isAnomalous = false;
            if (historicalLogs != null && historicalLogs.Count >= 10)
            {
                var pastScores = historicalLogs.Select(l => l.Score).ToList();
                double histMean = pastScores.Average();
                double histVariance = pastScores.Select(s => Math.Pow(s - histMean, 2)).Average();
                double histStdDev = Math.Sqrt(histVariance);

                if (histStdDev > 0.001)
                {
                    double anomalyZ = Math.Abs(confidenceScore - histMean) / histStdDev;
                    if (anomalyZ > 2.5)
                    {
                        isAnomalous = true;
                    }
                }
            }

            string resultMsg = isPassed 
                ? $"Biometric verification passed! Confidence: {confidenceScore:F1}% (Distance: {distance:F2}, User Threshold: {adaptiveThreshold:F2}, Pass Cutoff: {passCutoffConfidence:F1}%)" 
                : $"Biometric verification failed: Keystroke dynamics rhythm did not match your template. Confidence: {confidenceScore:F1}% (Distance: {distance:F2}, User Threshold: {adaptiveThreshold:F2}, Pass Cutoff: {passCutoffConfidence:F1}%)";

            return new VerificationResult
            {
                IsPassed = isPassed,
                ZScore = distance,
                ConfidenceScore = confidenceScore,
                AdaptiveThresholdScore = passCutoffConfidence,
                AdaptiveZThreshold = adaptiveThreshold,
                IsAnomalous = isAnomalous,
                Message = resultMsg
            };
        }
    }
}
