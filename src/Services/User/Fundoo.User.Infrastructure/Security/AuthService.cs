using Fundoo.User.Application.Interfaces;
using Fundoo.User.Application.Services;
using Fundoo.User.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using System.Security.Cryptography;
using static Fundoo.User.Application.Models.AuthResult;

namespace Fundoo.User.Infrastructure.Security
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly INotificationClient _notificationClient;
        private readonly ILogger<AuthService> _logger;
        private readonly TotpService _totp;                 // NEW
        private readonly IDataProtector _protector;         // NEW

        public AuthService(IUserRepository userRepository, IPasswordHasher passwordHasher,
            ITokenService tokenService, INotificationClient notificationClient, ILogger<AuthService> logger,
            TotpService totp, IDataProtectionProvider dataProtection)       // NEW: last 2 parameters
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _tokenService = tokenService;
            _notificationClient = notificationClient;
            _logger = logger;
            _totp = totp;
            _protector = dataProtection.CreateProtector("Fundoo.2FA.Secret");
        }

        public async Task<AuthResult> RegisterAsync(string firstName, string lastName, string email,
            string password, CancellationToken cancellationToken = default)
        {
            var normalizedEmail = email.Trim().ToUpperInvariant();

            var existingUser = await _userRepository.GetByNormalizedEmailAsync(normalizedEmail, cancellationToken);
            if (existingUser is not null)
            {
                return new AuthResult { IsSuccess = false, Message = "Email is already registered." };
            }

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                Email = email.Trim(),
                NormalizedEmail = normalizedEmail,
                PasswordHash = _passwordHasher.Hash(password),
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            await _userRepository.AddAsync(user, cancellationToken);
            await _userRepository.SaveChangesAsync(cancellationToken);

            return new AuthResult
            {
                IsSuccess = true,
                Message = "Registration successful.",
                AccessToken = _tokenService.CreateAccessToken(user),
                UserId = user.Id
            };
        }

        public async Task<AuthResult> LoginAsync(string email, string password,
            CancellationToken cancellationToken = default)
        {
            var normalizedEmail = email.Trim().ToUpperInvariant();

            var user = await _userRepository.GetByNormalizedEmailAsync(normalizedEmail, cancellationToken);

            if (user is null || !user.IsActive || !_passwordHasher.Verify(password, user.PasswordHash))
            {
                return new AuthResult { IsSuccess = false, Message = "Invalid email or password." };
            }

            // NEW: password sahi hai, par 2FA ON hai -> abhi access token NAHI, sirf chhota 2FA token
            if (user.TwoFactorEnabled)
            {
                return new AuthResult
                {
                    IsSuccess = true,
                    RequiresTwoFactor = true,
                    TwoFactorToken = _tokenService.CreateTwoFactorToken(user),
                    Message = "Two-factor code required."
                };
            }

            return new AuthResult
            {
                IsSuccess = true,
                Message = "Login successful.",
                AccessToken = _tokenService.CreateAccessToken(user),
                UserId = user.Id
            };
        }

        // NEW: login ka step 2 -> authenticator app ka code check karke asli access token do
        public async Task<AuthResult> VerifyTwoFactorLoginAsync(string twoFactorToken, string code,
            CancellationToken cancellationToken = default)
        {
            var invalid = new AuthResult { IsSuccess = false, Message = "Invalid or expired code." };

            var userId = _tokenService.ReadTwoFactorToken(twoFactorToken);
            if (userId is null)
                return invalid;

            var user = await _userRepository.GetByIdAsync(userId.Value, cancellationToken);
            if (user is null || !user.IsActive || !user.TwoFactorEnabled)
                return invalid;

            var secret = ReadSecret(user);
            if (secret is null)
                return invalid;

            var step = _totp.Verify(secret, code?.Trim() ?? "", user.TwoFactorLastStep);
            if (step is null)
                return invalid;

            user.TwoFactorLastStep = step.Value;          // ye code dobara use nahi ho sakta
            await _userRepository.SaveChangesAsync(cancellationToken);

            return new AuthResult
            {
                IsSuccess = true,
                Message = "Login successful.",
                AccessToken = _tokenService.CreateAccessToken(user),
                UserId = user.Id
            };
        }

        // NEW: 2FA setup, secret banao (abhi ON nahi hota)
        public async Task<TwoFactorSetupResult?> SetupTwoFactorAsync(Guid userId,
            CancellationToken cancellationToken = default)
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user is null || !user.IsActive || user.TwoFactorEnabled)
                return null;                               // pehle se ON hai to naya secret mat banao

            var secret = _totp.GenerateSecret();
            user.TwoFactorSecret = _protector.Protect(secret);   // encrypted save
            user.TwoFactorLastStep = 0;
            await _userRepository.SaveChangesAsync(cancellationToken);

            return new TwoFactorSetupResult(secret, _totp.BuildUri(user.Email, secret));
        }

        // NEW: user ka pehla code sahi ho to 2FA ON
        public async Task<bool> EnableTwoFactorAsync(Guid userId, string code,
            CancellationToken cancellationToken = default)
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user is null || !user.IsActive || user.TwoFactorEnabled)
                return false;

            var secret = ReadSecret(user);
            if (secret is null)
                return false;

            var step = _totp.Verify(secret, code?.Trim() ?? "", 0);
            if (step is null)
                return false;

            user.TwoFactorEnabled = true;
            user.TwoFactorLastStep = step.Value;
            await _userRepository.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<bool> DisableTwoFactorAsync(Guid userId, string password, string code,
    CancellationToken cancellationToken = default)
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user is null || !user.IsActive || !user.TwoFactorEnabled)
                return false;

            // 1) password sahi?
            if (!_passwordHasher.Verify(password ?? "", user.PasswordHash))
                return false;

            // 2) abhi ka code sahi? (naya hona chahiye, purana use hua code nahi chalega)
            var secret = ReadSecret(user);
            if (secret is null)
                return false;

            var step = _totp.Verify(secret, code?.Trim() ?? "", user.TwoFactorLastStep);
            if (step is null)
                return false;

            // 3) band karo aur secret hata do
            user.TwoFactorEnabled = false;
            user.TwoFactorSecret = null;
            user.TwoFactorLastStep = 0;
            user.UpdatedAtUtc = DateTime.UtcNow;

            await _userRepository.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
        {
            var normalizedEmail = email.Trim().ToUpperInvariant();

            var user = await _userRepository.GetByNormalizedEmailAsync(normalizedEmail, cancellationToken);

            // Do not reveal whether this email exists.
            if (user is null || !user.IsActive)
                return;

            var resetToken = _tokenService.CreatePasswordResetToken(user);

            try
            {
                await _notificationClient.SendPasswordResetAsync(user.Email, user.FirstName, resetToken, cancellationToken);
            }
            catch (BrokenCircuitException)
            {
                // Circuit open: Notification service abhi band hai, call gayi hi nahi.
                _logger.LogWarning("Notification circuit open, reset email skipped for {UserId}", user.Id);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Retry aur timeout ke baad bhi fail hua.
                _logger.LogError(ex, "Reset email could not be sent for {UserId}", user.Id);
            }
        }

        public async Task<bool> ResetPasswordAsync(string token, string newPassword,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(newPassword))
                return false;

            var claims = _tokenService.ValidatePasswordResetToken(token);
            if (claims is null)
                return false;

            var user = await _userRepository.GetByIdAsync(claims.UserId, cancellationToken);
            if (user is null || !user.IsActive)
                return false;

            // Token is valid only while the password is still the same one it was issued for.
            if (PasswordFingerprint.From(user.PasswordHash) != claims.Fingerprint)
                return false;

            user.PasswordHash = _passwordHasher.Hash(newPassword);
            user.UpdatedAtUtc = DateTime.UtcNow;

            await _userRepository.SaveChangesAsync(cancellationToken);
            return true;
        }

        // NEW: encrypted secret ko wapas padho (key badal gayi ho to null)
        private string? ReadSecret(AppUser user)
        {
            if (string.IsNullOrEmpty(user.TwoFactorSecret))
                return null;

            try { return _protector.Unprotect(user.TwoFactorSecret); }
            catch (CryptographicException) { return null; }
        }
        public async Task<AuthResult> LoginWithGoogleAsync(string googleId, string email, string firstName,
    string lastName, bool emailVerified, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(googleId) || string.IsNullOrWhiteSpace(email) || !emailVerified)
                return new AuthResult { IsSuccess = false, Message = "Google account email is not verified." };

            // 1) pehle Google id se dhundo (dobara login karne wala)
            var user = await _userRepository.GetByGoogleIdAsync(googleId, cancellationToken);

            if (user is null)
            {
                var normalizedEmail = email.Trim().ToUpperInvariant();

                // 2) isi email ka local account hai? to Google id link kar do
                user = await _userRepository.GetByNormalizedEmailAsync(normalizedEmail, cancellationToken);

                if (user is not null)
                {
                    user.GoogleId = googleId;
                    user.UpdatedAtUtc = DateTime.UtcNow;
                    await _userRepository.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    // 3) bilkul naya user: account bana do
                    user = new AppUser
                    {
                        Id = Guid.NewGuid(),
                        FirstName = (firstName ?? "").Trim(),
                        LastName = (lastName ?? "").Trim(),
                        Email = email.Trim(),
                        NormalizedEmail = normalizedEmail,
                        // Google user ke paas password nahi hota: random lambi value, jo koi guess nahi kar sakta
                        PasswordHash = _passwordHasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
                        GoogleId = googleId,
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    };

                    await _userRepository.AddAsync(user, cancellationToken);
                    await _userRepository.SaveChangesAsync(cancellationToken);
                }
            }

            if (!user.IsActive)
                return new AuthResult { IsSuccess = false, Message = "Account is disabled." };

            // 2FA ON hai to Google login ke baad bhi code maango (wahi purana Step 2)
            if (user.TwoFactorEnabled)
            {
                return new AuthResult
                {
                    IsSuccess = true,
                    RequiresTwoFactor = true,
                    TwoFactorToken = _tokenService.CreateTwoFactorToken(user),
                    Message = "Two-factor code required."
                };
            }

            return new AuthResult
            {
                IsSuccess = true,
                Message = "Login successful.",
                AccessToken = _tokenService.CreateAccessToken(user),
                UserId = user.Id
            };
        }
    }
}