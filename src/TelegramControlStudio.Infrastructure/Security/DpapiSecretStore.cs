using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace TelegramControlStudio.Infrastructure.Security;

[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TelegramControlStudio.LocalSecret.v1");

    public byte[] Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        return ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret),
            Entropy,
            DataProtectionScope.CurrentUser);
    }

    public string Unprotect(byte[] protectedSecret)
    {
        ArgumentNullException.ThrowIfNull(protectedSecret);
        var clear = ProtectedData.Unprotect(protectedSecret, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(clear);
    }
}
