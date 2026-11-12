using Fundoo.User.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Fundoo.User.Infrastructure.Security
{
    public class NotificationClient : INotificationClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<NotificationClient> _logger;

        public NotificationClient(IHttpClientFactory httpClientFactory, IConfiguration configuration,
            ILogger<NotificationClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendPasswordResetAsync(string email, string name, string resetToken,CancellationToken cancellationToken = default)
        {
            var client = _httpClientFactory.CreateClient("NotificationService");

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/notifications/password-reset")
            {
                Content = JsonContent.Create(new
                {
                    recipientEmail = email,
                    recipientName = name,
                    resetToken
                })
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateServiceToken());

            var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Notification service returned {StatusCode} for password reset mail.",
                    (int)response.StatusCode);
            }
        }

        // Short-lived token proving that this call comes from the User API.
        private string CreateServiceToken()
        {
            var signingKey = _configuration["ServiceAuth:SigningKey"]
                ?? throw new InvalidOperationException("ServiceAuth:SigningKey is not configured.");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));

            var token = new JwtSecurityToken(
                issuer: "fundoo-user-api",
                audience: "fundoo-notification-api",
                claims: new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, "fundoo-user-api"),
                    new Claim("scope", "notify")
                },
                expires: DateTime.UtcNow.AddMinutes(2),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
