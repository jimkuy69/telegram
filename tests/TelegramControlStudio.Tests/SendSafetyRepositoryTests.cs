using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class SendSafetyRepositoryTests
{
    [Fact]
    public async Task NewDatabaseStartsInTestModeWithKillSwitchOff()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-safety-default-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();

            var settings = await new SendSafetyRepository(path).GetAsync();

            Assert.True(settings.TestModeEnabled);
            Assert.False(settings.KillSwitchEngaged);
            Assert.Empty(settings.AllowlistUserIds);
            Assert.Equal(DateTimeOffset.UnixEpoch, settings.UpdatedAt);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task SaveAndReloadRoundTripsAllFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-safety-roundtrip-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var repository = new SendSafetyRepository(path);

            await repository.SaveAsync(new SendSafetySettings(false, new HashSet<long> { 55, 66 }, true, null));
            var settings = await repository.GetAsync();

            Assert.False(settings.TestModeEnabled);
            Assert.True(settings.KillSwitchEngaged);
            Assert.Equal(new long[] { 55, 66 }, settings.AllowlistUserIds.Order().ToArray());
            Assert.NotNull(settings.UpdatedAt);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task TogglesPreserveOtherFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-safety-toggle-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var repository = new SendSafetyRepository(path);
            await repository.SaveAllowlistAsync([7, 8]);

            await repository.SetKillSwitchAsync(true);
            var engaged = await repository.GetAsync();
            await repository.SetTestModeAsync(false);
            var live = await repository.GetAsync();

            Assert.True(engaged.KillSwitchEngaged);
            Assert.Equal(new long[] { 7, 8 }, engaged.AllowlistUserIds.Order().ToArray());
            Assert.True(live.KillSwitchEngaged);
            Assert.False(live.TestModeEnabled);
            Assert.Equal(2, live.AllowlistUserIds.Count);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task SaveAllowlistIgnoresNonPositiveIds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-safety-allowlist-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var repository = new SendSafetyRepository(path);

            await repository.SaveAllowlistAsync([10, 0, -5, 10]);
            var settings = await repository.GetAsync();

            Assert.Equal([10L], settings.AllowlistUserIds.ToArray());
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}
