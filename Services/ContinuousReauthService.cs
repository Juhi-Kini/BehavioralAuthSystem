using System;
using System.Threading.Tasks;
using BehavioralAuthSystem.Data;
using BehavioralAuthSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BehavioralAuthSystem.Services
{
    public interface IContinuousReauthService
    {
        string CurrentTrustLevel { get; }

        event Func<string, Task>? OnTrustLevelChanged;
        event Func<Task>? OnReauthRequired;

        Task ProcessContinuousSampleAsync(User user, KeystrokeSample sample);
        void ResetTrustLevel();
    }

    public class ContinuousReauthService : IContinuousReauthService
    {
        private readonly AppDbContext _dbContext;
        private readonly IKeystrokeService _keystrokeService;
        private readonly ILogger<ContinuousReauthService> _logger;

        public string CurrentTrustLevel { get; private set; } = "High";

        public event Func<string, Task>? OnTrustLevelChanged;
        public event Func<Task>? OnReauthRequired;

        // Number of consecutive failed samples.
        private int _consecutiveFailures = 0;

        // Require several failures before interrupting the user.
        private const int RequiredFailuresForReauth = 3;

        public ContinuousReauthService(
            AppDbContext dbContext,
            IKeystrokeService keystrokeService,
            ILogger<ContinuousReauthService> logger)
        {
            _dbContext = dbContext;
            _keystrokeService = keystrokeService;
            _logger = logger;
        }

        public void ResetTrustLevel()
        {
            CurrentTrustLevel = "High";
            _consecutiveFailures = 0;

            _ = NotifyTrustLevelChangedAsync();
        }

        private async Task NotifyTrustLevelChangedAsync()
        {
            if (OnTrustLevelChanged != null)
            {
                await OnTrustLevelChanged.Invoke(CurrentTrustLevel);
            }
        }

        public async Task ProcessContinuousSampleAsync(
            User user,
            KeystrokeSample sample)
        {
            if (user == null ||
                sample == null ||
                sample.HoldTimes == null ||
                sample.Latencies == null ||
                sample.HoldTimes.Count < 5)
            {
                return;
            }

            var loginPattern = await _dbContext.BehavioralPatterns
                .FirstOrDefaultAsync(
                    bp => bp.UserId == user.Id &&
                          bp.ActionType == "login");

            if (loginPattern == null)
            {
                return;
            }

            var bioResult = _keystrokeService.VerifySample(
                sample,
                loginPattern);

            string previousLevel = CurrentTrustLevel;

            // ---------------------------------------------------------
            // GOOD SAMPLE
            // ---------------------------------------------------------
            if (bioResult.IsPassed &&
                bioResult.ConfidenceScore >= 55.0)
            {
                CurrentTrustLevel = "High";

                // A good sample resets the failure streak.
                _consecutiveFailures = 0;
            }
            // ---------------------------------------------------------
            // BORDERLINE SAMPLE
            // ---------------------------------------------------------
            else if (bioResult.IsPassed)
            {
                CurrentTrustLevel = "ElevatedRisk";

                // Don't immediately ask for the password.
                // Keep monitoring.
                _consecutiveFailures = 0;
            }
            // ---------------------------------------------------------
            // FAILED SAMPLE
            // ---------------------------------------------------------
            else
            {
                _consecutiveFailures++;

                _logger.LogWarning(
                    "Continuous verification failed for User {UserId}. " +
                    "Failure streak: {FailureCount}/{RequiredFailures}. " +
                    "Confidence: {Score:F1}%",
                    user.Id,
                    _consecutiveFailures,
                    RequiredFailuresForReauth,
                    bioResult.ConfidenceScore);

                // Only require re-authentication after several
                // consecutive failures.
                if (_consecutiveFailures >= RequiredFailuresForReauth)
                {
                    CurrentTrustLevel = "VerificationRequired";
                }
                else
                {
                    CurrentTrustLevel = "ElevatedRisk";
                }
            }

            _logger.LogInformation(
                "Continuous Re-Auth Evaluated: User {UserId}, " +
                "Confidence: {Score:F1}%, Status: {TrustLevel}, " +
                "FailureStreak: {FailureCount}",
                user.Id,
                bioResult.ConfidenceScore,
                CurrentTrustLevel,
                _consecutiveFailures);

            // Notify UI only when trust level actually changes.
            if (CurrentTrustLevel != previousLevel &&
                OnTrustLevelChanged != null)
            {
                await OnTrustLevelChanged.Invoke(CurrentTrustLevel);
            }

            // IMPORTANT:
            // Trigger the popup only when we FIRST enter
            // VerificationRequired.
            //
            // This prevents the popup from firing repeatedly
            // on every subsequent failed sample.
            if (CurrentTrustLevel == "VerificationRequired" &&
                previousLevel != "VerificationRequired" &&
                OnReauthRequired != null)
            {
                await OnReauthRequired.Invoke();
            }
        }
    }
}