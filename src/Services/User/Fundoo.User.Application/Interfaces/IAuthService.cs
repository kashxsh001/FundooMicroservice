using Fundoo.User.Application.Models;
using System;
using System.Collections.Generic;
using System.Text;
using static Fundoo.User.Application.Models.AuthResult;
namespace Fundoo.User.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(string firstName, string lastName, string email, string password,
        CancellationToken cancellationToken = default);

    Task<AuthResult> LoginAsync(string email, string password,
        CancellationToken cancellationToken = default);

    Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> ResetPasswordAsync(string token, string newPassword,
        CancellationToken cancellationToken = default);
    Task<AuthResult> VerifyTwoFactorLoginAsync(string twoFactorToken, string code, CancellationToken cancellationToken = default);
    Task<TwoFactorSetupResult?> SetupTwoFactorAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> EnableTwoFactorAsync(Guid userId, string code, CancellationToken cancellationToken = default);
    Task<AuthResult> LoginWithGoogleAsync(string googleId, string email, string firstName, string lastName,
    bool emailVerified, CancellationToken cancellationToken = default);
}

public class AuthResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? AccessToken { get; init; }
    public Guid? UserId { get; init; }
    public bool RequiresTwoFactor { get; set; }
    public string? TwoFactorToken { get; set; }


}
   