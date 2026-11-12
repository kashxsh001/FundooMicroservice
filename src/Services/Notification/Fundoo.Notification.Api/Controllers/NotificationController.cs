using Fundoo.Notification.Application.Abstractions;
using Fundoo.Notification.Application.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fundoo.Notification.Api.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    [Authorize(Policy = "InternalOnly")]
    public sealed class NotificationController(INotificationService notificationService) : ControllerBase
    {
        [HttpPost("password-reset")]
        public async Task<IActionResult> SendPasswordReset([FromBody] PasswordResetNotificationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.RecipientEmail) ||
                string.IsNullOrWhiteSpace(request.ResetToken))
            {
                return BadRequest(new { isSuccess = false, message = "Recipient email and reset token are required." });
            }

            await notificationService.SendPasswordResetNotificationAsync(request);

            return Accepted(new { isSuccess = true, message = "Password-reset notification accepted." });
        }
    }
}
