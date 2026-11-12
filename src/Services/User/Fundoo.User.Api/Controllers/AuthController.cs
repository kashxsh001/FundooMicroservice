using Fundoo.User.Api.Contracts;
using Fundoo.User.Application.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Fundoo.User.Api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService) => _authService = authService;

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _authService.RegisterAsync(
                request.FirstName, request.LastName, request.Email, request.Password, cancellationToken);

            if (!result.IsSuccess)
                return Conflict(new { isSuccess = false, message = result.Message });

            return Ok(new
            {
                isSuccess = true,
                message = result.Message,
                userId = result.UserId,
                accessToken = result.AccessToken
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request,
     CancellationToken cancellationToken)
        {
            var result = await _authService.LoginAsync(request.Email, request.Password, cancellationToken);

            if (!result.IsSuccess)
                return Unauthorized(new { isSuccess = false, message = result.Message });

            return Ok(new
            {
                isSuccess = true,
                message = result.Message,
                userId = result.UserId,
                accessToken = result.AccessToken,
                requiresTwoFactor = result.RequiresTwoFactor,     // NEW
                twoFactorToken = result.TwoFactorToken            // NEW
            });
        }
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request,
    CancellationToken cancellationToken)
        {
            await _authService.ForgotPasswordAsync(request.Email, cancellationToken);

            return Ok(new
            {
                isSuccess = true,
                message = "If the account exists, a password reset token will be sent to the registered email."
            });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request,
            CancellationToken cancellationToken)
        {
            var isReset = await _authService.ResetPasswordAsync(
                request.Token, request.NewPassword, cancellationToken);

            if (!isReset)
                return BadRequest(new
                {
                    isSuccess = false,
                    message = "The password reset token is invalid or has expired."
                });

            return Ok(new { isSuccess = true, message = "Password has been reset successfully." });
        }
        public record TwoFactorCodeRequest(string Code);
        public record TwoFactorLoginRequest(string TwoFactorToken, string Code);

        private Guid? CurrentUserId()
        {
            var sub = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(sub, out var id) ? id : null;
        }

        [Authorize]
        [HttpPost("2fa/setup")]
        public async Task<IActionResult> SetupTwoFactor(CancellationToken cancellationToken)
        {
            var userId = CurrentUserId();
            if (userId is null) return Unauthorized();

            var result = await _authService.SetupTwoFactorAsync(userId.Value, cancellationToken);

            if (result is null)
                return BadRequest(new { isSuccess = false, message = "2FA is already enabled or user not found." });

            return Ok(new
            {
                isSuccess = true,
                message = "Add this key in your authenticator app, then call /2fa/enable with the 6-digit code.",
                secret = result.Secret,
                otpauthUri = result.OtpAuthUri
            });
        }

        [Authorize]
        [HttpPost("2fa/enable")]
        public async Task<IActionResult> EnableTwoFactor([FromBody] TwoFactorCodeRequest request,
            CancellationToken cancellationToken)
        {
            var userId = CurrentUserId();
            if (userId is null) return Unauthorized();

            var enabled = await _authService.EnableTwoFactorAsync(userId.Value, request.Code, cancellationToken);

            return enabled
                ? Ok(new { isSuccess = true, message = "Two-factor authentication enabled." })
                : BadRequest(new { isSuccess = false, message = "Invalid code." });
        }

        [AllowAnonymous]
        [HttpPost("login/2fa")]
        public async Task<IActionResult> LoginTwoFactor([FromBody] TwoFactorLoginRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _authService.VerifyTwoFactorLoginAsync(
                request.TwoFactorToken, request.Code, cancellationToken);

            if (!result.IsSuccess)
                return Unauthorized(new { isSuccess = false, message = result.Message });

            return Ok(new
            {
                isSuccess = true,
                message = result.Message,
                userId = result.UserId,
                accessToken = result.AccessToken
            });
        }

        public record DisableTwoFactorRequest(string Password, string Code);

        [Authorize]
        [HttpPost("2fa/disable")]
        public async Task<IActionResult> DisableTwoFactor([FromBody] DisableTwoFactorRequest request,
            CancellationToken cancellationToken)
        {
            var userId = CurrentUserId();
            if (userId is null) return Unauthorized();

            var disabled = await _authService.DisableTwoFactorAsync(
                userId.Value, request.Password, request.Code, cancellationToken);

            return disabled
                ? Ok(new { isSuccess = true, message = "Two-factor authentication disabled." })
                : BadRequest(new { isSuccess = false, message = "Invalid password or code, or 2FA is not enabled." });
        }
        // Step 1: browser yahan aata hai, hum Google par bhej dete hain
        [HttpGet("google/login")]
        public IActionResult GoogleLogin()
            => Challenge(new AuthenticationProperties { RedirectUri = "/api/auth/google/callback" }, "Google");

        // Step 2: Google se wapas aane ke baad
        [HttpGet("google/callback")]
        public async Task<IActionResult> GoogleCallback(CancellationToken cancellationToken)
        {
            var external = await HttpContext.AuthenticateAsync("External");
            if (!external.Succeeded || external.Principal is null)
                return Unauthorized(new { isSuccess = false, message = "Google sign-in failed." });

            var p = external.Principal;
            var googleId = p.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = p.FindFirst(ClaimTypes.Email)?.Value;
            var firstName = p.FindFirst(ClaimTypes.GivenName)?.Value ?? "";
            var lastName = p.FindFirst(ClaimTypes.Surname)?.Value ?? "";
            var verified = string.Equals(p.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);

            await HttpContext.SignOutAsync("External");      // temporary cookie hata do

            var result = await _authService.LoginWithGoogleAsync(
                googleId ?? "", email ?? "", firstName, lastName, verified, cancellationToken);

            if (!result.IsSuccess)
                return Unauthorized(new { isSuccess = false, message = result.Message });

            return Ok(new
            {
                isSuccess = true,
                message = result.Message,
                userId = result.UserId,
                accessToken = result.AccessToken,
                requiresTwoFactor = result.RequiresTwoFactor,
                twoFactorToken = result.TwoFactorToken
            });
        }
    }
}