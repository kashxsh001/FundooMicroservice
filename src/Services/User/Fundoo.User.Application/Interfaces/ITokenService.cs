using Fundoo.User.Application.Services;
using Fundoo.User.Domain;

namespace Fundoo.User.Application.Interfaces
{
    public interface ITokenService
    {
        string CreateAccessToken(AppUser user);
        string CreatePasswordResetToken(AppUser user);
        PasswordResetClaims? ValidatePasswordResetToken(string token);
        string CreateTwoFactorToken(AppUser user);
        Guid? ReadTwoFactorToken(string token);
    }
}