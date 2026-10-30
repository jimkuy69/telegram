using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.Infrastructure.Hosting;

public sealed record StudioOptions(string DatabasePath)
{
    public static StudioOptions CreateDefault() => new(AppPaths.ResolveDatabasePath());
}

/// <summary>
/// Shared bootstrap for every host (desktop, web): resolves the database, runs migrations, and owns the TDLib client registry.
/// </summary>
public sealed class StudioHost : IAsyncDisposable
{
    private bool _disposed;

    private StudioHost(string databasePath)
    {
        DatabasePath = databasePath;
        TdLibClients = new TdLibAccountClientRegistry();
    }

    public string DatabasePath { get; }

    public TdLibAccountClientRegistry TdLibClients { get; }

    public static async Task<StudioHost> StartAsync(StudioOptions? options = null, CancellationToken cancellationToken = default)
    {
        var resolved = options ?? StudioOptions.CreateDefault();
        if (string.IsNullOrWhiteSpace(resolved.DatabasePath))
            throw new ArgumentException("Database path is required.", nameof(options));

        var host = new StudioHost(resolved.DatabasePath);
        try
        {
            await new SqliteDatabase(host.DatabasePath).InitializeAsync(cancellationToken);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await TdLibClients.DisposeAllAsync();
    }
}
