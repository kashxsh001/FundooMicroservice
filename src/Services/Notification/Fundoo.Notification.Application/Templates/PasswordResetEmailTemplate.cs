using System.Net;

namespace Fundoo.Notification.Application.Templates
{
    public static class PasswordResetEmailTemplate
    {
        public static string Build(string recipientName, string resetToken)
        {
            var safeName = WebUtility.HtmlEncode(recipientName);
            var safeToken = WebUtility.HtmlEncode(resetToken);

            return $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="UTF-8"><title>Fundoo Password Reset</title></head>
            <body style="font-family: Arial, sans-serif;">
                <h2>Password Reset Request</h2>
                <p>Hello {safeName},</p>
                <p>We received a request to reset your Fundoo Notes password.</p>
                <p>Copy this reset token and use it in the reset-password API:</p>
                <div style="padding:12px; background:#f2f2f2; word-break:break-all; font-family:monospace;">
                    {safeToken}
                </div>
                <p>This token expires in 15 minutes and works only once.</p>
                <p>If you did not request this, you can ignore this email.</p>
                <p>Regards,<br>Fundoo Notes Team</p>
            </body>
            </html>
            """;
        }
    }
}
