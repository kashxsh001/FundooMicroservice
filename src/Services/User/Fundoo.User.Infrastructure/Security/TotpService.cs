using OtpNet;

namespace Fundoo.User.Infrastructure.Security;

public class TotpService
{
    // naya random secret
    public string GenerateSecret() =>
        Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    // authenticator app ke liye link (QR banane ke kaam aata hai)
    public string BuildUri(string email, string secret) =>
        $"otpauth://totp/Fundoo:{Uri.EscapeDataString(email)}?secret={secret}&issuer=Fundoo";

    // code sahi hai? sahi hai to uska "time step" lautata hai, galat hai to null
    public long? Verify(string secret, string code, long lastUsedStep)
    {
        var totp = new Totp(Base32Encoding.ToBytes(secret));

        var ok = totp.VerifyTotp(code, out long step,
                     new VerificationWindow(previous: 1, future: 1));   // +-30 sec

        return ok && step > lastUsedStep ? step : null;                 // purana/dohraya code reject
    }
}