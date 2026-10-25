using System.Runtime.Versioning;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

[SupportedOSPlatform("windows")]
public sealed class TdLibConfigurationRepositoryTests
{
    [Fact]
    public async Task StoresAndReadsConfigurationPerAccount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tcs-tdlib-config-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accounts = new AccountRepository(path);
            var first = await accounts.CreateAsync("First", "+66000000031");
            var second = await accounts.CreateAsync("Second", "+66000000032");
            var repository = new TdLibConfigurationRepository(path);
            await repository.SaveAsync(first, 123456, "hash-one");

            var config = await repository.GetAsync(first);
            Assert.NotNull(config);
            Assert.Equal(123456, config.Value.ApiId);
            Assert.Equal("hash-one", config.Value.ApiHash);
            Assert.Null(await repository.GetAsync(second));
            Assert.True(await repository.DeleteAsync(first));
            Assert.Null(await repository.GetAsync(first));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

