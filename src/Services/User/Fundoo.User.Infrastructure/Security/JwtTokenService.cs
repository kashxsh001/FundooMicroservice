using Fundoo.User.Application.Interfaces;
using Fundoo.User.Application.Services;
using Fundoo.User.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using JwtRegisteredClaimNames = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames;

namespace Fundoo.User.Infrastructure.Security
{
    public class JwtTokenService : ITokenService
    {
        private const string ResetAudience = "fundoo-password-reset";
        private const string PurposeClaim = "purpose";
        private const string PurposeValue = "password_reset";
        private const string FingerprintClaim = "pwd";
        private const int ResetExpiryMinutes = 15;
        private const string TwoFactorAudience = "fundoo-2fa";

        private readonly IConfiguration _configuration;

        public JwtTokenService(IConfiguration configuration) => _configuration = configuration;

        public string CreateAccessToken(AppUser user)
        {
            var audience = Required("Jwt:Audience");
            var expiryMinutes = int.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "30");

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email)
            };

            return WriteToken(audience, claims, TimeSpan.FromMinutes(expiryMinutes));
        }

        public string CreatePasswordResetToken(AppUser user)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(PurposeClaim, PurposeValue),
                new Claim(FingerprintClaim, PasswordFingerprint.From(user.PasswordHash))
            };

            return WriteToken(ResetAudience, claims, TimeSpan.FromMinutes(ResetExpiryMinutes));
        }

        public PasswordResetClaims? ValidatePasswordResetToken(string token)
        {
            var signingKey = Required("Jwt:SigningKey");
            var issuer = Required("Jwt:Issuer");

            try
            {
                new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = ResetAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out var validated);

                var jwt = (JwtSecurityToken)validated;

                var purpose = jwt.Claims.FirstOrDefault(c => c.Type == PurposeClaim)?.Value;
                var fingerprint = jwt.Claims.FirstOrDefault(c => c.Type == FingerprintClaim)?.Value;

                if (purpose != PurposeValue || string.IsNullOrEmpty(fingerprint) ||
                    !Guid.TryParse(jwt.Subject, out var userId))
                {
                    return null;
                }

                return new PasswordResetClaims(userId, fingerprint);
            }
            catch (Exception)
            {
                return null;   // expired, tampered, wrong audience, etc.
            }
        }

        private string WriteToken(string audience, IEnumerable<Claim> claims, TimeSpan lifetime)
        {
            var signingKey = Required("Jwt:SigningKey");
            var issuer = Required("Jwt:Issuer");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.Add(lifetime),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string Required(string key)=> _configuration[key]?? throw new InvalidOperationException($"{key} is not configured.");

        public string CreateTwoFactorToken(AppUser user)
        {
            var claims = new[]
            {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(PurposeClaim, "2fa")
    };

            // alag audience + sirf 5 minute, to ye access token ki tarah chal hi nahi sakta
            return WriteToken(TwoFactorAudience, claims, TimeSpan.FromMinutes(5));
        }

        // token sahi hai to userId, warna null (tumhare ValidatePasswordResetToken jaisa)
        public Guid? ReadTwoFactorToken(string token)
        {
            var signingKey = Required("Jwt:SigningKey");
            var issuer = Required("Jwt:Issuer");

            try
            {
                new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = TwoFactorAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out var validated);

                var jwt = (JwtSecurityToken)validated;

                var purpose = jwt.Claims.FirstOrDefault(c => c.Type == PurposeClaim)?.Value;
                if (purpose != "2fa" || !Guid.TryParse(jwt.Subject, out var userId))
                    return null;

                return userId;
            }
            catch (Exception)
            {
                return null;   // expired, tampered, wrong audience, etc.
            }
        }
    }

}