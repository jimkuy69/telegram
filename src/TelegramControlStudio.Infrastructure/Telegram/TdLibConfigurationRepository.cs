using System.Runtime.Versioning;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Security;

namespace TelegramControlStudio.Infrastructure.Telegram;

[SupportedOSPlatform("windows")]
public sealed class TdLibConfigurationRepository(string databasePath)
{
    private readonly AccountSecretRepository _secrets = new(databasePath, new DpapiSecretStore());

    public async Task SaveAsync(long accountId, int apiId, string apiHash, CancellationToken cancellationToken = default)
    {
        if (apiId <= 0) throw new ArgumentOutOfRangeException(nameof(apiId));
        ArgumentException.ThrowIfNullOrWhiteSpace(apiHash);
        await _secrets.SaveAsync(accountId, "tdlib.api_id", apiId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        await _secrets.SaveAsync(accountId, "tdlib.api_hash", apiHash, cancellationToken);
    }

    public async Task<(int ApiId, string ApiHash)?> GetAsync(long accountId, CancellationToken cancellationToken = default)
    {
        var id = await _secrets.GetAsync(accountId, "tdlib.api_id", cancellationToken);
        var hash = await _secrets.GetAsync(accountId, "tdlib.api_hash", cancellationToken);
        if (id is null || hash is null || !int.TryParse(id, out var apiId) || apiId <= 0) return null;
        return (apiId, hash);
    }

    public async Task<bool> DeleteAsync(long accountId, CancellationToken cancellationToken = default) =>
        await _secrets.DeleteAsync(accountId, "tdlib.api_id", cancellationToken) |
        await _secrets.DeleteAsync(accountId, "tdlib.api_hash", cancellationToken) |
        await _secrets.DeleteAsync(accountId, "tdlib.native_directory", cancellationToken) |
        await _secrets.DeleteAsync(accountId, "tdlib.native_sha256", cancellationToken);

    public async Task SaveNativeLocationAsync(long accountId, string nativeDirectory, string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        await _secrets.SaveAsync(accountId, "tdlib.native_directory", Path.GetFullPath(nativeDirectory), cancellationToken);
        await _secrets.SaveAsync(accountId, "tdlib.native_sha256", sha256, cancellationToken);
    }

    public async Task<(string Directory, string Sha256)?> GetNativeLocationAsync(long accountId, CancellationToken cancellationToken = default)
    {
        var directory = await _secrets.GetAsync(accountId, "tdlib.native_directory", cancellationToken);
        var sha256 = await _secrets.GetAsync(accountId, "tdlib.native_sha256", cancellationToken);
        return directory is null || sha256 is null ? null : (directory, sha256);
    }
}

