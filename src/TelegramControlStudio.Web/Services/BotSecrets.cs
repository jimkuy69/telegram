using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Web.Services;

/// <summary>Windows-guarded access to the DPAPI-protected bot token.</summary>
public static class BotSecrets
{
    public static bool IsSupported => OperatingSystem.IsWindows();

    public static async Task<string?> GetTokenAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            return await new AppSecretRepository(databasePath).GetBotTokenAsync(cancellationToken);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public static async Task SaveTokenAsync(string databasePath, string token, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Bot token storage requires Windows DPAPI.");
        await new AppSecretRepository(databasePath).SaveBotTokenAsync(token, cancellationToken);
    }
}
