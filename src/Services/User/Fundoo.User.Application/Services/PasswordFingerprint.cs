using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Fundoo.User.Application.Services
{
    public static class PasswordFingerprint
    {
        public static string From(string passwordHash)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)))[..16];
    }

    public sealed record PasswordResetClaims(Guid UserId, string Fingerprint);
}

