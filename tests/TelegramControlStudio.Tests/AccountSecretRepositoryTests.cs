using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Security;
using Xunit;

namespace TelegramControlStudio.Tests;

[SupportedOSPlatform("windows")]
public sealed class AccountSecretRepositoryTests
{
    [Fact]
    public async Task ProtectsAndIsolatesSecretsPerAccount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tcs-secret-{Guid.NewGuid():N}.db");
        await new SqliteDatabase(path).InitializeAsync();
        var accounts = new AccountRepository(path);
        var first = await accounts.CreateAsync("First", "+66000000021");
        var second = await accounts.CreateAsync("Second", "+66000000022");
        var repository = new AccountSecretRepository(path, new DpapiSecretStore());

        await repository.SaveAsync(first, "api_hash", "hash-first");
        await repository.SaveAsync(second, "api_hash", "hash-second");

        Assert.Equal("hash-first", await repository.GetAsync(first, "api_hash"));
        Assert.Equal("hash-second", await repository.GetAsync(second, "api_hash"));
        Assert.Null(await repository.GetAsync(first, "missing"));

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT protected_value FROM account_secrets WHERE account_id = $id";
        command.Parameters.AddWithValue("$id", first);
        var stored = (byte[]?)await command.ExecuteScalarAsync();
        Assert.NotNull(stored);
        Assert.NotEqual("hash-first", System.Text.Encoding.UTF8.GetString(stored!));
        Assert.True(await repository.DeleteAsync(first, "api_hash"));
        Assert.Null(await repository.GetAsync(first, "api_hash"));
    }
}

