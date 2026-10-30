using Microsoft.Data.Sqlite;
using TelegramControlStudio.Infrastructure.Hosting;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class StudioHostTests
{
    [Fact]
    public async Task StartAsyncCreatesDatabaseAndExposesRegistry()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-host-{Guid.NewGuid():N}.db");
        try
        {
            await using var host = await StudioHost.StartAsync(new StudioOptions(path));

            Assert.Equal(path, host.DatabasePath);
            Assert.True(File.Exists(path));
            Assert.Equal(0, host.TdLibClients.Count);
            var safety = await new SendSafetyRepository(host.DatabasePath).GetAsync();
            Assert.True(safety.TestModeEnabled);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task DisposeAsyncIsIdempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-host-idem-{Guid.NewGuid():N}.db");
        try
        {
            var host = await StudioHost.StartAsync(new StudioOptions(path));
            await host.DisposeAsync();
            await host.DisposeAsync();
            Assert.Equal(0, host.TdLibClients.Count);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task StartAsyncRejectsBlankDatabasePath()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => StudioHost.StartAsync(new StudioOptions(" ")));
    }

    [Fact]
    public void DefaultOptionsFollowEnvironmentOverride()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-host-env-{Guid.NewGuid():N}.db");
        var previous = Environment.GetEnvironmentVariable(AppPaths.DatabasePathOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DatabasePathOverrideVariable, path);
            var options = StudioOptions.CreateDefault();
            Assert.Equal(Path.GetFullPath(path), options.DatabasePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DatabasePathOverrideVariable, previous);
        }
    }
}
