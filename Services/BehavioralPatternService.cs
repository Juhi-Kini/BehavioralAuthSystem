using System.Text.Json;
using BehavioralAuthSystem.Data;
using BehavioralAuthSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace BehavioralAuthSystem.Services;

public interface IBehavioralPatternService
{
    Task<BehavioralPattern?> GetPatternAsync(int userId, string actionType);

    Task<List<BehavioralPattern>> GetPatternsAsync(int userId);

    Task<BehavioralPattern> SavePatternAsync(
        int userId,
        string actionType,
        string patternType,
        BehavioralCapture capture);

    Task DeletePatternAsync(int userId, string actionType);

    Task<(bool Passed, string Message, double Distance)> VerifyAsync(
        int userId,
        string actionType,
        BehavioralCapture capture);
}

public class BehavioralPatternService : IBehavioralPatternService
{
    private readonly AppDbContext _db;

    public BehavioralPatternService(AppDbContext db)
    {
        _db = db;
    }

    public Task<BehavioralPattern?> GetPatternAsync(
        int userId,
        string actionType)
    {
        return _db.BehavioralPatterns
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.UserId == userId &&
                x.ActionType == actionType);
    }

    public Task<List<BehavioralPattern>> GetPatternsAsync(int userId)
    {
        return _db.BehavioralPatterns
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.ActionType)
            .ToListAsync();
    }

    public async Task<BehavioralPattern> SavePatternAsync(
        int userId,
        string actionType,
        string patternType,
        BehavioralCapture capture)
    {
        var existing = await _db.BehavioralPatterns
            .FirstOrDefaultAsync(x =>
                x.UserId == userId &&
                x.ActionType == actionType);

        if (existing == null)
        {
            existing = new BehavioralPattern
            {
                UserId = userId,
                ActionType = actionType
            };

            _db.BehavioralPatterns.Add(existing);
        }

        /*
         * IMPORTANT:
         *
         * We only save the behavioral information that is required
         * for verification.
         *
         * We DO NOT save:
         * - Mouse coordinates
         * - Shape coordinates
         * - Exact drawing path
         *
         * Coordinates/path are only used temporarily by JavaScript
         * to identify the interaction.
         */

        BehavioralCapture storedCapture;

        switch (patternType)
        {
            case "Mouse Clicks":

                storedCapture = new BehavioralCapture
                {
                    PatternType = "Mouse Clicks",
                    ClickType = capture.ClickType,
                    ClickCount = capture.Clicks.Count
                };

                break;

            case "Shape":

                storedCapture = new BehavioralCapture
                {
                    PatternType = "Shape",
                    ShapeType = capture.ShapeType
                };

                break;

            case "Shortcut":

                storedCapture = new BehavioralCapture
                {
                    PatternType = "Shortcut",
                    Shortcut = new ShortcutCapture
                    {
                        Ctrl = capture.Shortcut.Ctrl,
                        Shift = capture.Shortcut.Shift,
                        Alt = capture.Shortcut.Alt,
                        Meta = capture.Shortcut.Meta,
                        Key = capture.Shortcut.Key
                    }
                };

                break;

            case "Key Sequence":

                storedCapture = new BehavioralCapture
                {
                    PatternType = "Key Sequence",
                    KeySequence = capture.KeySequence
                        .Select(NormalizeKey)
                        .ToList()
                };

                break;

            default:

                throw new InvalidOperationException(
                    $"Unsupported behavioral pattern type: {patternType}");
        }

        existing.PatternType = patternType;
        existing.PatternData = JsonSerializer.Serialize(storedCapture);
        existing.CreatedAt = DateTime.UtcNow;

        /*
         * Keep legacy timing fields populated so existing
         * analytics/database code does not break.
         */
        if (capture.Clicks.Count > 1)
        {
            var intervals = capture.Clicks
                .Zip(
                    capture.Clicks.Skip(1),
                    (a, b) => Math.Max(0, b.T - a.T))
                .ToList();

            existing.MeanLatency =
                intervals.Count == 0
                    ? 0
                    : intervals.Average();

            existing.StdDevLatency = StdDev(intervals);
        }
        else
        {
            existing.MeanLatency = 0;
            existing.StdDevLatency = 0;
        }

        await _db.SaveChangesAsync();

        return existing;
    }

    public async Task DeletePatternAsync(
        int userId,
        string actionType)
    {
        var pattern = await _db.BehavioralPatterns
            .FirstOrDefaultAsync(x =>
                x.UserId == userId &&
                x.ActionType == actionType);

        if (pattern != null)
        {
            _db.BehavioralPatterns.Remove(pattern);

            await _db.SaveChangesAsync();
        }
    }

    public async Task<(bool Passed, string Message, double Distance)> VerifyAsync(
        int userId,
        string actionType,
        BehavioralCapture capture)
    {
        var pattern = await GetPatternAsync(userId, actionType);

        if (pattern == null)
        {
            return await Audit(
                userId,
                actionType,
                false,
                1,
                "No behavioral pattern is enrolled for this action.");
        }

        BehavioralCapture? enrolled;

        try
        {
            enrolled = JsonSerializer.Deserialize<BehavioralCapture>(
                pattern.PatternData);
        }
        catch
        {
            enrolled = null;
        }

        if (enrolled == null)
        {
            return await Audit(
                userId,
                actionType,
                false,
                1,
                "The enrolled behavioral pattern could not be read.");
        }

        if (!string.Equals(
                pattern.PatternType,
                capture.PatternType,
                StringComparison.OrdinalIgnoreCase))
        {
            return await Audit(
                userId,
                actionType,
                false,
                1,
                "Pattern type mismatch.");
        }

        bool passed;
        double distance;
        string message;

        switch (capture.PatternType)
        {
            // =========================================================
            // MOUSE CLICKS
            // =========================================================

            case "Mouse Clicks":

                var expectedCount = enrolled.ClickCount;
                var actualCount = capture.Clicks.Count;

                var expectedClickType =
                    NormalizeClickType(enrolled.ClickType);

                var actualClickType =
                    NormalizeClickType(capture.ClickType);

                passed =
                    expectedCount == actualCount &&
                    string.Equals(
                        expectedClickType,
                        actualClickType,
                        StringComparison.OrdinalIgnoreCase);

                distance =
                    Math.Abs(actualCount - expectedCount);

                if (!string.Equals(
                        expectedClickType,
                        actualClickType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    distance += 1;
                }

                message = passed
                    ? "Behavioral interaction verified."
                    : "The behavioral interaction did not match your enrolled profile.";

                break;


            // =========================================================
            // SHAPE
            // =========================================================

            case "Shape":

                var expectedShape =
                    NormalizeShape(enrolled.ShapeType);

                var actualShape =
                    NormalizeShape(capture.ShapeType);

                passed =
                    !string.IsNullOrWhiteSpace(expectedShape) &&
                    string.Equals(
                        expectedShape,
                        actualShape,
                        StringComparison.OrdinalIgnoreCase);

                distance = passed ? 0 : 1;

                message = passed
                    ? "Behavioral interaction verified."
                    : "The behavioral interaction did not match your enrolled profile.";

                break;


            // =========================================================
            // SHORTCUT
            // =========================================================

            case "Shortcut":

                passed =
                    enrolled.Shortcut.Ctrl ==
                    capture.Shortcut.Ctrl

                    &&

                    enrolled.Shortcut.Shift ==
                    capture.Shortcut.Shift

                    &&

                    enrolled.Shortcut.Alt ==
                    capture.Shortcut.Alt

                    &&

                    enrolled.Shortcut.Meta ==
                    capture.Shortcut.Meta

                    &&

                    string.Equals(
                        NormalizeKey(enrolled.Shortcut.Key),
                        NormalizeKey(capture.Shortcut.Key),
                        StringComparison.OrdinalIgnoreCase);

                distance = passed ? 0 : 1;

                message = passed
                    ? "Behavioral interaction verified."
                    : "The behavioral interaction did not match your enrolled profile.";

                break;


            // =========================================================
            // KEY SEQUENCE
            // =========================================================

            case "Key Sequence":

                var expectedSequence =
                    enrolled.KeySequence
                        .Select(NormalizeKey)
                        .ToList();

                var actualSequence =
                    capture.KeySequence
                        .Select(NormalizeKey)
                        .ToList();

                passed =
                    expectedSequence.Count > 0 &&
                    expectedSequence.SequenceEqual(
                        actualSequence,
                        StringComparer.OrdinalIgnoreCase);

                distance = passed ? 0 : 1;

                message = passed
                    ? "Behavioral interaction verified."
                    : "The behavioral interaction did not match your enrolled profile.";

                break;


            // =========================================================
            // OLD SCREEN TAP
            // =========================================================

            case "Screen Tap":

                return await Audit(
                    userId,
                    actionType,
                    false,
                    1,
                    "Screen Tap is no longer supported. Please enroll a new behavioral pattern.");


            default:

                return await Audit(
                    userId,
                    actionType,
                    false,
                    1,
                    "Unsupported behavioral pattern type.");
        }

        return await Audit(
            userId,
            actionType,
            passed,
            distance,
            message);
    }

    private async Task<(bool Passed, string Message, double Distance)> Audit(
        int userId,
        string actionType,
        bool passed,
        double distance,
        string message)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId);

        if (user != null)
        {
            _db.AuthLogs.Add(new AuthLog
            {
                UserId = userId,
                Username = user.Username,

                IsSuccessful = passed,

                Score = passed ? 100 : 0,

                // Keep this finite.
                // Do NOT use double.MaxValue.
                ZScore = double.IsFinite(distance)
                    ? distance
                    : 1,

                AdaptiveThreshold = 0,

                AttemptTime = DateTime.UtcNow,

                IsSuspicious = !passed,

                ActionType = "action_verify",

                FailureReason =
                    passed
                        ? string.Empty
                        : message
            });

            await _db.SaveChangesAsync();
        }

        return (passed, message, distance);
    }

    private static string NormalizeClickType(string? value)
    {
        if (string.Equals(
                value,
                "right",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Right";
        }

        return "Left";
    }

    private static string NormalizeShape(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Length == 1
            ? value.ToUpperInvariant()
            : value.Trim();
    }

    private static double StdDev(List<double> values)
    {
        if (values.Count < 2)
            return 0;

        var mean = values.Average();

        return Math.Sqrt(
            values.Sum(v => Math.Pow(v - mean, 2))
            / values.Count);
    }
}