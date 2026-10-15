using System;
using System.Collections.Generic;
using BehavioralAuthSystem.Models;
using BehavioralAuthSystem.Services;
using Xunit;

namespace BehavioralAuthSystem.Tests
{
    public class KeystrokeServiceTests
    {
        private readonly IKeystrokeService _keystrokeService;

        public KeystrokeServiceTests()
        {
            _keystrokeService = new KeystrokeService();
        }

        private static KeystrokeSample CreateConsistentSample(double baseHold = 100.0, double baseLat = 150.0)
        {
            return new KeystrokeSample
            {
                HoldTimes = new List<double> { baseHold, baseHold + 2, baseHold - 1, baseHold + 1, baseHold },
                Latencies = new List<double> { baseLat, baseLat - 2, baseLat + 3, baseLat - 1 },
                KeyCount = 5,
                TotalDuration = (5 * baseHold) + (4 * baseLat)
            };
        }

        [Fact]
        public void Enrollment_WithExactlyFourConsistentSamples_Succeeds()
        {
            // Arrange
            var samples = new List<KeystrokeSample>
            {
                CreateConsistentSample(100.0, 150.0),
                CreateConsistentSample(102.0, 148.0),
                CreateConsistentSample(99.0, 152.0),
                CreateConsistentSample(101.0, 150.0)
            };

            // Act
            var (isValid, message, pattern, cv) = _keystrokeService.ProcessEnrollment(1, "login", samples);

            // Assert
            Assert.True(isValid);
            Assert.NotNull(pattern);
            Assert.True(cv <= 0.40, $"Expected CV <= 0.40, got {cv}");
            Assert.Equal("login", pattern.ActionType);
            Assert.Equal(1, pattern.UserId);
            Assert.Contains("created successfully", message);
        }

        [Fact]
        public void Enrollment_WithFewerThanFourSamples_Fails()
        {
            // Arrange
            var samples = new List<KeystrokeSample>
            {
                CreateConsistentSample(100.0, 150.0),
                CreateConsistentSample(102.0, 148.0),
                CreateConsistentSample(99.0, 152.0)
            };

            // Act
            var (isValid, message, pattern, _) = _keystrokeService.ProcessEnrollment(1, "login", samples);

            // Assert
            Assert.False(isValid);
            Assert.Null(pattern);
            Assert.Contains("requires exactly 4", message);
        }

        [Fact]
        public void Enrollment_WithMoreThanFourSamples_Fails()
        {
            // Arrange
            var samples = new List<KeystrokeSample>
            {
                CreateConsistentSample(100.0, 150.0),
                CreateConsistentSample(102.0, 148.0),
                CreateConsistentSample(99.0, 152.0),
                CreateConsistentSample(101.0, 150.0),
                CreateConsistentSample(100.0, 150.0)
            };

            // Act
            var (isValid, message, pattern, _) = _keystrokeService.ProcessEnrollment(1, "login", samples);

            // Assert
            Assert.False(isValid);
            Assert.Null(pattern);
            Assert.Contains("requires exactly 4", message);
        }

        [Fact]
        public void Enrollment_WithMismatchedKeyCount_Fails()
        {
            // Arrange: Sample 4 has 6 keys instead of 5
            var samples = new List<KeystrokeSample>
            {
                CreateConsistentSample(100.0, 150.0),
                CreateConsistentSample(102.0, 148.0),
                CreateConsistentSample(99.0, 152.0),
                new KeystrokeSample
                {
                    HoldTimes = new List<double> { 100, 100, 100, 100, 100, 100 },
                    Latencies = new List<double> { 150, 150, 150, 150, 150 },
                    KeyCount = 6
                }
            };

            // Act
            var (isValid, message, pattern, _) = _keystrokeService.ProcessEnrollment(1, "login", samples);

            // Assert
            Assert.False(isValid);
            Assert.Null(pattern);
            Assert.Contains("Key count mismatch", message);
        }

        [Fact]
        public void Enrollment_WithExcessiveCV_Rejected()
        {
            // Arrange: Wildly divergent typing rhythms causing CV > 0.40
            var samples = new List<KeystrokeSample>
            {
                new KeystrokeSample { HoldTimes = new List<double> { 50, 60, 55 }, Latencies = new List<double> { 100, 110 } },
                new KeystrokeSample { HoldTimes = new List<double> { 250, 290, 270 }, Latencies = new List<double> { 500, 520 } },
                new KeystrokeSample { HoldTimes = new List<double> { 40, 45, 50 }, Latencies = new List<double> { 90, 95 } },
                new KeystrokeSample { HoldTimes = new List<double> { 350, 390, 370 }, Latencies = new List<double> { 700, 750 } }
            };

            // Act
            var (isValid, message, pattern, cv) = _keystrokeService.ProcessEnrollment(1, "login", samples);

            // Assert
            Assert.False(isValid);
            Assert.Null(pattern);
            Assert.True(cv > 0.40, $"Expected CV > 0.40, got {cv}");
            Assert.Contains("rejected", message.ToLowerInvariant());
        }

        [Fact]
        public void Enrollment_WithNegativeOrNonFiniteTimings_Fails()
        {
            // Arrange: Negative and NaN values
            var samples = new List<KeystrokeSample>
            {
                CreateConsistentSample(100.0, 150.0),
                new KeystrokeSample { HoldTimes = new List<double> { -10.0, 100, 100, 100, 100 }, Latencies = new List<double> { 150, 150, 150, 150 } },
                CreateConsistentSample(99.0, 152.0),
                CreateConsistentSample(101.0, 150.0)
            };

            // Act
            var (isValid, message, pattern, _) = _keystrokeService.ProcessEnrollment(1, "login", samples);

            // Assert
            Assert.False(isValid);
            Assert.Null(pattern);
            Assert.Contains("Invalid or out-of-range", message);
        }

        [Fact]
        public void Verification_WithMatchingSample_PassesAndExceedsCutoff()
        {
            // Arrange
            var enrollSamples = new List<KeystrokeSample>
            {
                CreateConsistentSample(100.0, 150.0),
                CreateConsistentSample(101.0, 149.0),
                CreateConsistentSample(99.0, 151.0),
                CreateConsistentSample(100.0, 150.0)
            };
            var (_, _, pattern, _) = _keystrokeService.ProcessEnrollment(1, "login", enrollSamples);
            Assert.NotNull(pattern);

            var testSample = CreateConsistentSample(100.5, 150.2);

            // Act
            var result = _keystrokeService.VerifySample(testSample, pattern);

            // Assert
            Assert.True(result.IsPassed);
            Assert.True(result.ConfidenceScore >= (100.0 / Math.E));
            Assert.True(result.ZScore <= result.AdaptiveZThreshold);
        }

        [Fact]
        public void Verification_WithSeverelyDeviantTiming_Fails()
        {
            // Arrange
            var enrollSamples = new List<KeystrokeSample>
            {
                CreateConsistentSample(100.0, 150.0),
                CreateConsistentSample(101.0, 149.0),
                CreateConsistentSample(99.0, 151.0),
                CreateConsistentSample(100.0, 150.0)
            };
            var (_, _, pattern, _) = _keystrokeService.ProcessEnrollment(1, "login", enrollSamples);
            Assert.NotNull(pattern);

            // Impostor sample with completely different timing
            var impostorSample = new KeystrokeSample
            {
                HoldTimes = new List<double> { 350.0, 400.0, 20.0, 500.0, 30.0 },
                Latencies = new List<double> { 800.0, 50.0, 900.0, 40.0 }
            };

            // Act
            var result = _keystrokeService.VerifySample(impostorSample, pattern);

            // Assert
            Assert.False(result.IsPassed);
            Assert.True(result.ConfidenceScore < (100.0 / Math.E));
            Assert.True(result.ZScore > result.AdaptiveZThreshold);
        }

        [Fact]
        public void Verification_WithTruncatedOrMismatchedSequenceLength_FailsImmediately()
        {
            // Arrange
            var enrollSamples = new List<KeystrokeSample>
            {
                CreateConsistentSample(100.0, 150.0),
                CreateConsistentSample(101.0, 149.0),
                CreateConsistentSample(99.0, 151.0),
                CreateConsistentSample(100.0, 150.0)
            };
            var (_, _, pattern, _) = _keystrokeService.ProcessEnrollment(1, "login", enrollSamples);
            Assert.NotNull(pattern);

            // Attacker typed only 2 keys
            var truncatedSample = new KeystrokeSample
            {
                HoldTimes = new List<double> { 100.0, 100.0 },
                Latencies = new List<double> { 150.0 }
            };

            // Act
            var result = _keystrokeService.VerifySample(truncatedSample, pattern);

            // Assert
            Assert.False(result.IsPassed);
            Assert.Equal(0.0, result.ConfidenceScore);
            Assert.Contains("does not match enrolled template", result.Message);
        }

        [Fact]
        public void Verification_WithCorruptedPatternData_FailsGracefully()
        {
            // Arrange
            var corruptedPattern = new BehavioralPattern
            {
                UserId = 1,
                ActionType = "login",
                PatternData = "{ NOT_VALID_JSON }"
            };
            var sample = CreateConsistentSample();

            // Act
            var result = _keystrokeService.VerifySample(sample, corruptedPattern);

            // Assert
            Assert.False(result.IsPassed);
            Assert.Contains("Corrupted", result.Message);
        }

        [Fact]
        public void AnomalyDetection_FlagsDeviationsGreaterThan2Point5StdDev()
        {
            // Arrange
            var enrollSamples = new List<KeystrokeSample>
            {
                CreateConsistentSample(100.0, 150.0),
                CreateConsistentSample(101.0, 149.0),
                CreateConsistentSample(99.0, 151.0),
                CreateConsistentSample(100.0, 150.0)
            };
            var (_, _, pattern, _) = _keystrokeService.ProcessEnrollment(1, "login", enrollSamples);
            Assert.NotNull(pattern);

            // 15 historical successful logins around 92% confidence with small variance
            var history = new List<AuthLog>();
            for (int i = 0; i < 15; i++)
            {
                history.Add(new AuthLog
                {
                    UserId = 1,
                    ActionType = "login",
                    IsSuccessful = true,
                    Score = 92.0 + (i % 2 == 0 ? 0.3 : -0.3)
                });
            }

            // A sample that still barely passes (e.g. 40%) will be > 2.5 std deviations from ~92%
            var deviantSample = new KeystrokeSample
            {
                HoldTimes = new List<double> { 115.0, 116.0, 114.0, 115.0, 115.0 },
                Latencies = new List<double> { 185.0, 186.0, 184.0, 185.0 }
            };

            // Act
            var result = _keystrokeService.VerifySample(deviantSample, pattern, history);

            // Assert
            Assert.True(result.IsAnomalous, "Expected anomalous flag to be true for > 2.5 stddev deviation from history");
        }
    }
}
