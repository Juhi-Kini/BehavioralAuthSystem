namespace BehavioralAuthSystem.Services
{
    public static class AuthServiceExtensions
    {
        public static string HashPassword(this IAuthService _, string password)
            => AuthService.HashPassword(password);

        public static bool VerifyPassword(this IAuthService _, string password, string storedHash)
            => AuthService.VerifyPassword(password, storedHash);
    }
}
