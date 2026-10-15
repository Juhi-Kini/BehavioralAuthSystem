using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using BehavioralAuthSystem.Models;
using BehavioralAuthSystem.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace BehavioralAuthSystem.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        public class LoginRequest
        {
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public KeystrokeSample? KeystrokeSample { get; set; }
        }

        public class LoginResponse
        {
            public bool Success { get; set; }
            public bool IsLockedOut { get; set; }
            public string Message { get; set; } = string.Empty;
            public int? UserId { get; set; }
            public string? Username { get; set; }
            public VerificationResult? BiometricResult { get; set; }
        }

        /// <summary>
        /// Secure login endpoint that performs full server-side validation of password credentials
        /// and keystroke dynamics biometrics before issuing a session cookie.
        /// Unauthenticated requests supplying arbitrary UserIds are strictly prohibited.
        /// </summary>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "Username and password are required."
                });
            }

            if (request.KeystrokeSample == null || request.KeystrokeSample.HoldTimes == null || request.KeystrokeSample.HoldTimes.Count == 0)
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "Keystroke dynamics timing sample is required."
                });
            }

            var result = await _authService.LoginAsync(request.Username, request.Password, request.KeystrokeSample);

            if (!result.IsSuccess || result.User == null)
            {
                return Unauthorized(new LoginResponse
                {
                    Success = false,
                    IsLockedOut = result.IsLockedOut,
                    Message = result.Message,
                    BiometricResult = result.BiometricResult
                });
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, result.User.Id.ToString()),
                new Claim(ClaimTypes.Name, result.User.Username)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            return Ok(new LoginResponse
            {
                Success = true,
                Message = "Authentication successful.",
                UserId = result.User.Id,
                Username = result.User.Username,
                BiometricResult = result.BiometricResult
            });
        }

        [HttpPost("sign-out")]
        public async Task<IActionResult> SignOutCookie()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new { success = true });
        }

        [HttpGet("sign-out")]
        public async Task<IActionResult> SignOutCookieGet()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/login");
        }

        [HttpGet("current-user")]
        public IActionResult GetCurrentUser()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return Ok(new
                {
                    isAuthenticated = true,
                    userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    username = User.Identity.Name
                });
            }

            return Ok(new { isAuthenticated = false });
        }
    }
}
