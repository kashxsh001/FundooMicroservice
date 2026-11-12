namespace Fundoo.User.Application.Models;

public class AuthResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? Token { get; set; }

    public static AuthResult SuccessResult(string message, string? token = null)
    {
        return new AuthResult
        {
            Success = true,
            Message = message,
            Token = token
        };
    }

    public static AuthResult FailureResult(string message)
    {
        return new AuthResult
        {
            Success = false,
            Message = message
        };
    }

    public bool RequiresTwoFactor { get; set; }
    public string? TwoFactorToken { get; set; }
    public record TwoFactorSetupResult(string Secret, string OtpAuthUri);
}
