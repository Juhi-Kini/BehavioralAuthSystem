using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Net.Mail;
using System.Threading.Tasks;
using BehavioralAuthSystem.Data;
using BehavioralAuthSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BehavioralAuthSystem.Services
{
    public class LoginResult
    {
        public bool IsSuccess { get; set; }

        public bool IsLockedOut { get; set; }

        public string Message { get; set; } = string.Empty;

        public User? User { get; set; }

        public VerificationResult? BiometricResult { get; set; }
    }


    public interface IAuthService
    {
        /*
         * REGISTRATION
         *
         * Only login/password biometric enrollment is performed here.
         *
         * Username
         * Password
         * 4 password typing samples
         *
         * Action behavioral profiles are enrolled later
         * from the Behavioral Actions page.
         */
        Task<(bool IsSuccess, string Message, User? User)> RegisterAsync(
            string username,
            string email,
            string password,
            List<KeystrokeSample> loginSamples);

        Task<(bool Success, string Message)> RequestPasswordResetAsync(string email);

        Task<(bool Success, string Message)> ResetPasswordAsync(
            string email,
            string code,
            string newPassword,
            List<KeystrokeSample> loginSamples);

        Task<(bool Success, string Message)> UpdateRecoveryEmailAsync(int userId, string email);


        Task<LoginResult> LoginAsync(
            string username,
            string password,
            KeystrokeSample keystrokeSample);


        Task<User?> GetUserByUsernameAsync(
            string username);


        Task<User?> GetUserByIdAsync(
            int userId);


        Task<List<AuthLog>> GetUserAuthLogsAsync(
            int userId);


        Task<BehavioralPattern?> GetUserPatternAsync(
            int userId,
            string actionType);
    }


    public class AuthService : IAuthService
    {
        private readonly AppDbContext _dbContext;

        private readonly IKeystrokeService _keystrokeService;

        private readonly IEncryptionService _encryptionService;

        private readonly ILogger<AuthService> _logger;

        private readonly IEmailService _emailService;


        private const int MaxFailedAttempts = 5;

        private const int LockoutMinutes = 15;


        public AuthService(
            AppDbContext dbContext,
            IKeystrokeService keystrokeService,
            IEncryptionService encryptionService,
            ILogger<AuthService> logger,
            IEmailService emailService)
        {
            _dbContext = dbContext;

            _keystrokeService = keystrokeService;

            _encryptionService = encryptionService;

            _logger = logger;

            _emailService = emailService;
        }


        // ============================================================
        // REGISTER
        // ============================================================

        public async Task<(bool IsSuccess, string Message, User? User)> RegisterAsync(
            string username,
            string email,
            string password,
            List<KeystrokeSample> loginSamples)
        {
            // --------------------------------------------------------
            // Validate username
            // --------------------------------------------------------

            if (string.IsNullOrWhiteSpace(username)
                || username.Length < 3)
            {
                return (
                    false,
                    "Username must be at least 3 characters long.",
                    null
                );
            }


            // --------------------------------------------------------
            // Validate password
            // --------------------------------------------------------

            if (string.IsNullOrWhiteSpace(password)
                || password.Length < 6)
            {
                return (
                    false,
                    "Password must be at least 6 characters long.",
                    null
                );
            }


            // --------------------------------------------------------
            // Validate recovery email
            // --------------------------------------------------------

            if (!IsValidEmail(email))
            {
                return (
                    false,
                    "Please enter a valid recovery email address.",
                    null
                );
            }


            // --------------------------------------------------------
            // Check duplicate email
            // --------------------------------------------------------

            var existingEmail =
                await _dbContext.Users
                    .FirstOrDefaultAsync(
                        u => u.Email != null &&
                             u.Email.ToLower() == email.ToLower());

            if (existingEmail != null)
            {
                return (
                    false,
                    "That recovery email is already registered.",
                    null
                );
            }


            // --------------------------------------------------------
            // Validate biometric samples
            // --------------------------------------------------------

            if (loginSamples == null
                || loginSamples.Count < 4)
            {
                return (
                    false,
                    "Registration requires 4 password typing samples.",
                    null
                );
            }


            // --------------------------------------------------------
            // Check duplicate username
            // --------------------------------------------------------

            var existingUser =
                await _dbContext.Users
                    .FirstOrDefaultAsync(
                        u => u.Username.ToLower() ==
                             username.ToLower());


            if (existingUser != null)
            {
                return (
                    false,
                    "Username is already taken. Please choose another.",
                    null
                );
            }


            // --------------------------------------------------------
            // Process LOGIN keystroke enrollment
            // --------------------------------------------------------
            //
            // This creates ONLY the login behavioral profile.
            //
            // There is NO action phrase profile here.
            //

            var (
                isLoginPatternValid,
                loginPatternMsg,
                loginPattern,
                loginCV
            ) =
                _keystrokeService.ProcessEnrollment(
                    0,
                    "login",
                    loginSamples);


            if (!isLoginPatternValid
                || loginPattern == null)
            {
                return (
                    false,
                    $"Password Biometrics: {loginPatternMsg}",
                    null
                );
            }


            // --------------------------------------------------------
            // Generate encryption salt
            // --------------------------------------------------------

            byte[] encryptionSalt =
                _encryptionService.GenerateSalt();


            // --------------------------------------------------------
            // Hash password
            // --------------------------------------------------------

            string passwordHash =
                HashPassword(password);


            // --------------------------------------------------------
            // Create user
            // --------------------------------------------------------
            //
            // ActionPhraseHash is intentionally left empty.
            //
            // We are NOT using the old Action Phrase system.
            //
            // The database column can remain for now so we don't
            // need to change the database just for registration.
            //

            var newUser = new User
            {
                Username = username,

                Email = email.Trim().ToLowerInvariant(),

                EmailVerifiedAt = null,

                PasswordHash = passwordHash,

                ActionPhraseHash = string.Empty,

                EncryptionSalt = encryptionSalt,

                CreatedAt = DateTime.UtcNow,

                FailedLoginAttempts = 0,

                LockoutUntil = null
            };


            // --------------------------------------------------------
            // Save user first
            // --------------------------------------------------------

            _dbContext.Users.Add(newUser);

            await _dbContext.SaveChangesAsync();


            // --------------------------------------------------------
            // Assign actual UserId to login biometric profile
            // --------------------------------------------------------

            loginPattern.UserId = newUser.Id;


            // --------------------------------------------------------
            // Save LOGIN behavioral profile
            // --------------------------------------------------------

            _dbContext.BehavioralPatterns.Add(loginPattern);

            await _dbContext.SaveChangesAsync();


            // --------------------------------------------------------
            // Log registration
            // --------------------------------------------------------

            _logger.LogInformation(
                "Successfully registered user {Username} with ID {UserId}.",
                newUser.Username,
                newUser.Id);


            return (
                true,
                "Registration successful! You may now log in.",
                newUser
            );
        }


        // ============================================================
        // LOGIN
        // ============================================================

        public async Task<LoginResult> LoginAsync(
            string username,
            string password,
            KeystrokeSample keystrokeSample)
        {
            // --------------------------------------------------------
            // Validate input
            // --------------------------------------------------------

            if (string.IsNullOrWhiteSpace(username)
                || string.IsNullOrWhiteSpace(password))
            {
                return new LoginResult
                {
                    IsSuccess = false,

                    Message =
                        "Username and password are required."
                };
            }


            // --------------------------------------------------------
            // Find user
            // --------------------------------------------------------

            var user =
                await _dbContext.Users
                    .Include(u => u.BehavioralPatterns)
                    .FirstOrDefaultAsync(
                        u => u.Username.ToLower() ==
                             username.ToLower());


            if (user == null)
            {
                await LogAuthAttemptAsync(
                    null,
                    username,
                    false,
                    0.0,
                    0.0,
                    0.0,
                    false,
                    "login",
                    "Invalid username or password.");


                return new LoginResult
                {
                    IsSuccess = false,

                    Message =
                        "Invalid username or password."
                };
            }


            // --------------------------------------------------------
            // Account lockout check
            // --------------------------------------------------------

            if (user.LockoutUntil.HasValue
                && user.LockoutUntil.Value > DateTime.UtcNow)
            {
                TimeSpan remaining =
                    user.LockoutUntil.Value -
                    DateTime.UtcNow;


                string lockoutMsg =
                    $"Account is temporarily locked due to multiple failed attempts. " +
                    $"Please try again in {Math.Ceiling(remaining.TotalMinutes)} minutes.";


                await LogAuthAttemptAsync(
                    user.Id,
                    user.Username,
                    false,
                    0.0,
                    0.0,
                    0.0,
                    true,
                    "login",
                    lockoutMsg);


                return new LoginResult
                {
                    IsSuccess = false,

                    IsLockedOut = true,

                    Message = lockoutMsg,

                    User = user
                };
            }


            // --------------------------------------------------------
            // Verify password
            // --------------------------------------------------------

            if (!VerifyPassword(
                    password,
                    user.PasswordHash))
            {
                user.FailedLoginAttempts++;


                string failReason =
                    "Invalid password.";


                if (user.FailedLoginAttempts >=
                    MaxFailedAttempts)
                {
                    user.LockoutUntil =
                        DateTime.UtcNow.AddMinutes(
                            LockoutMinutes);


                    failReason =
                        $"Invalid password. Account locked for " +
                        $"{LockoutMinutes} minutes.";
                }


                await _dbContext.SaveChangesAsync();


                await LogAuthAttemptAsync(
                    user.Id,
                    user.Username,
                    false,
                    0.0,
                    0.0,
                    0.0,
                    false,
                    "login",
                    failReason);


                return new LoginResult
                {
                    IsSuccess = false,

                    IsLockedOut =
                        user.LockoutUntil.HasValue,

                    Message = failReason,

                    User = user
                };
            }


            // --------------------------------------------------------
            // Password correct
            // Reset failed attempts
            // --------------------------------------------------------

            user.FailedLoginAttempts = 0;

            user.LockoutUntil = null;

            await _dbContext.SaveChangesAsync();


            // --------------------------------------------------------
            // Find LOGIN behavioral profile
            // --------------------------------------------------------

            var loginPattern =
                user.BehavioralPatterns
                    .FirstOrDefault(
                        bp => bp.ActionType == "login");


            if (loginPattern == null)
            {
                await LogAuthAttemptAsync(
                    user.Id,
                    user.Username,
                    false,
                    0.0,
                    0.0,
                    0.0,
                    false,
                    "login",
                    "Missing biometric template.");


                return new LoginResult
                {
                    IsSuccess = false,

                    Message =
                        "Biometric template missing for account.",

                    User = user
                };
            }


            // --------------------------------------------------------
            // Get historical successful login attempts
            // --------------------------------------------------------

            var historicalLogs =
                await _dbContext.AuthLogs
                    .Where(
                        al =>
                            al.UserId == user.Id
                            && al.ActionType == "login"
                            && al.IsSuccessful)
                    .OrderByDescending(
                        al => al.AttemptTime)
                    .Take(20)
                    .ToListAsync();


            // --------------------------------------------------------
            // Verify keystroke behavior
            // --------------------------------------------------------

            var bioResult =
                _keystrokeService.VerifySample(
                    keystrokeSample,
                    loginPattern,
                    historicalLogs);


            // --------------------------------------------------------
            // Log authentication attempt
            // --------------------------------------------------------

            await LogAuthAttemptAsync(
                user.Id,
                user.Username,
                bioResult.IsPassed,
                bioResult.ConfidenceScore,
                bioResult.ZScore,
                bioResult.AdaptiveThresholdScore,
                bioResult.IsAnomalous,
                "login",
                bioResult.Message);


            // --------------------------------------------------------
            // Biometric failed
            // --------------------------------------------------------

            if (!bioResult.IsPassed)
            {
                return new LoginResult
                {
                    IsSuccess = false,

                    Message = bioResult.Message,

                    User = user,

                    BiometricResult = bioResult
                };
            }


            // --------------------------------------------------------
            // Everything passed
            // --------------------------------------------------------

            return new LoginResult
            {
                IsSuccess = true,

                Message =
                    "Authentication successful.",

                User = user,

                BiometricResult = bioResult
            };
        }


        // ============================================================
        // PASSWORD RESET / RECOVERY
        // ============================================================

        public async Task<(bool Success, string Message)> RequestPasswordResetAsync(string email)
        {
            if (!IsValidEmail(email))
                return (false, "Please enter the recovery email address.");

            var normalizedEmail = email.Trim().ToLowerInvariant();

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail);

            // Do not reveal whether an email is registered.
            if (user == null)
                return (true, "If an account exists for that email, a verification code has been sent.");

            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");

            user.PasswordResetCodeHash = HashResetCode(code);
            user.PasswordResetExpiresAt = DateTime.UtcNow.AddMinutes(10);
            await _dbContext.SaveChangesAsync();

            var emailResult = await _emailService.SendPasswordResetCodeAsync(
                user.Email!,
                user.Username,
                code);

            if (!emailResult.Success)
            {
                user.PasswordResetCodeHash = null;
                user.PasswordResetExpiresAt = null;
                await _dbContext.SaveChangesAsync();
                return (false, emailResult.Message);
            }

            return (true, emailResult.Message);
        }


        public async Task<(bool Success, string Message)> ResetPasswordAsync(
            string email,
            string code,
            string newPassword,
            List<KeystrokeSample> loginSamples)
        {
            if (!IsValidEmail(email))
                return (false, "Invalid recovery email address.");

            if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
                return (false, "Enter the 6-digit verification code.");

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                return (false, "Password must be at least 6 characters long.");

            if (loginSamples == null || loginSamples.Count < 4)
                return (false, "Please complete all 4 new password typing samples.");

            var normalizedEmail = email.Trim().ToLowerInvariant();

            var user = await _dbContext.Users
                .Include(u => u.BehavioralPatterns)
                .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail);

            if (user == null ||
                string.IsNullOrWhiteSpace(user.PasswordResetCodeHash) ||
                !user.PasswordResetExpiresAt.HasValue ||
                user.PasswordResetExpiresAt.Value < DateTime.UtcNow ||
                !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(user.PasswordResetCodeHash),
                    Convert.FromHexString(HashResetCode(code))))
            {
                return (false, "The verification code is invalid or expired.");
            }

            var (
                isPatternValid,
                patternMessage,
                loginPattern,
                _
            ) = _keystrokeService.ProcessEnrollment(user.Id, "login", loginSamples);

            if (!isPatternValid || loginPattern == null)
                return (false, $"Password typing verification: {patternMessage}");

            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                var oldLoginPatterns = user.BehavioralPatterns
                    .Where(p => p.ActionType == "login")
                    .ToList();

                if (oldLoginPatterns.Count > 0)
                    _dbContext.BehavioralPatterns.RemoveRange(oldLoginPatterns);

                loginPattern.UserId = user.Id;
                _dbContext.BehavioralPatterns.Add(loginPattern);

                user.PasswordHash = HashPassword(newPassword);
                user.PasswordResetCodeHash = null;
                user.PasswordResetExpiresAt = null;
                user.EmailVerifiedAt = DateTime.UtcNow;
                user.FailedLoginAttempts = 0;
                user.LockoutUntil = null;

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                return (true, "Password reset successfully. Your new keystroke rhythm has also been enrolled.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }


        public async Task<(bool Success, string Message)> UpdateRecoveryEmailAsync(int userId, string email)
        {
            if (!IsValidEmail(email))
                return (false, "Please enter a valid email address.");

            var normalizedEmail = email.Trim().ToLowerInvariant();

            var duplicate = await _dbContext.Users
                .FirstOrDefaultAsync(u =>
                    u.Email != null &&
                    u.Email.ToLower() == normalizedEmail &&
                    u.Id != userId);

            if (duplicate != null)
                return (false, "That email address is already associated with another account.");

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return (false, "User account could not be found.");

            user.Email = normalizedEmail;
            user.EmailVerifiedAt = null;
            await _dbContext.SaveChangesAsync();

            return (true, "Recovery email saved. Email ownership is verified when you complete a password reset.");
        }


        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var address = new MailAddress(email.Trim());
                return address.Address.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }


        private static string HashResetCode(string code)
        {
            var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code));
            return Convert.ToHexString(bytes);
        }


        // ============================================================
        // GET USER BY USERNAME
        // ============================================================

        public async Task<User?> GetUserByUsernameAsync(
            string username)
        {
            return await _dbContext.Users
                .Include(u => u.BehavioralPatterns)
                .FirstOrDefaultAsync(
                    u => u.Username.ToLower() ==
                         username.ToLower());
        }


        // ============================================================
        // GET USER BY ID
        // ============================================================

        public async Task<User?> GetUserByIdAsync(
            int userId)
        {
            return await _dbContext.Users
                .Include(u => u.BehavioralPatterns)
                .FirstOrDefaultAsync(
                    u => u.Id == userId);
        }


        // ============================================================
        // GET USER AUTH LOGS
        // ============================================================

        public async Task<List<AuthLog>> GetUserAuthLogsAsync(
            int userId)
        {
            return await _dbContext.AuthLogs
                .Where(
                    al => al.UserId == userId)
                .OrderByDescending(
                    al => al.AttemptTime)
                .ToListAsync();
        }


        // ============================================================
        // GET USER BEHAVIORAL PATTERN
        // ============================================================

        public async Task<BehavioralPattern?> GetUserPatternAsync(
            int userId,
            string actionType)
        {
            return await _dbContext.BehavioralPatterns
                .FirstOrDefaultAsync(
                    bp =>
                        bp.UserId == userId
                        && bp.ActionType == actionType);
        }


        // ============================================================
        // AUDIT LOG
        // ============================================================

        private async Task LogAuthAttemptAsync(
        int? userId,
        string username,
        bool isSuccessful,
        double score,
        double zScore,
        double adaptiveThreshold,
        bool isSuspicious,
        string actionType,
        string failureReason,
        string? evaluationLabel = null)
        {
            var log = new AuthLog
            {
                UserId = userId,
                Username = username,
                IsSuccessful = isSuccessful,
                Score = score,
                ZScore = zScore,
                AdaptiveThreshold = adaptiveThreshold,
                AttemptTime = DateTime.UtcNow,
                IsSuspicious = isSuspicious,
                ActionType = actionType,
                FailureReason = failureReason,
                EvaluationLabel = evaluationLabel
            };

            _dbContext.AuthLogs.Add(log);

            await _dbContext.SaveChangesAsync();
        }


        // ============================================================
        // PASSWORD HASHING
        // ============================================================

        public static string HashPassword(
            string password)
        {
            byte[] salt =
                RandomNumberGenerator.GetBytes(16);


            using var pbkdf2 =
                new Rfc2898DeriveBytes(
                    password,
                    salt,
                    100000,
                    HashAlgorithmName.SHA256);


            byte[] hash =
                pbkdf2.GetBytes(32);


            byte[] hashBytes =
                new byte[48];


            Buffer.BlockCopy(
                salt,
                0,
                hashBytes,
                0,
                16);


            Buffer.BlockCopy(
                hash,
                0,
                hashBytes,
                16,
                32);


            return Convert.ToBase64String(
                hashBytes);
        }


        // ============================================================
        // PASSWORD VERIFICATION
        // ============================================================

        public static bool VerifyPassword(
            string password,
            string storedHash)
        {
            try
            {
                byte[] hashBytes =
                    Convert.FromBase64String(
                        storedHash);


                if (hashBytes.Length != 48)
                    return false;


                byte[] salt =
                    new byte[16];


                Buffer.BlockCopy(
                    hashBytes,
                    0,
                    salt,
                    0,
                    16);


                using var pbkdf2 =
                    new Rfc2898DeriveBytes(
                        password,
                        salt,
                        100000,
                        HashAlgorithmName.SHA256);


                byte[] hash =
                    pbkdf2.GetBytes(32);


                return CryptographicOperations
                    .FixedTimeEquals(
                        hashBytes.AsSpan(16, 32),
                        hash);
            }
            catch
            {
                return false;
            }
        }
    }
}