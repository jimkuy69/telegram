using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class BotLoginCodeRepositoryTests
{
    [Fact]
    public async Task CodeIsConsumedOnce()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var repository = new BotLoginCodeRepository(path);

            var code = await repository.CreateAsync(4242);
            Assert.Equal(32, code.Length);

            Assert.Equal(4242, await repository.ConsumeAsync(code, DateTimeOffset.UtcNow));
            Assert.Null(await repository.ConsumeAsync(code, DateTimeOffset.UtcNow));
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task ExpiredAndUnknownCodesAreRejected()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var repository = new BotLoginCodeRepository(path);

            var code = await repository.CreateAsync(4242, TimeSpan.FromMinutes(-1));
            Assert.Null(await repository.ConsumeAsync(code, DateTimeOffset.UtcNow));
            Assert.Null(await repository.ConsumeAsync("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", DateTimeOffset.UtcNow));
            Assert.Null(await repository.ConsumeAsync("  ", DateTimeOffset.UtcNow));
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task CodeIsNotStoredInPlainText()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var code = await new BotLoginCodeRepository(path).CreateAsync(4242);

            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT code_hash FROM bot_login_codes;";
            var stored = (string)(await command.ExecuteScalarAsync())!;

            Assert.NotEqual(code, stored);
            Assert.DoesNotContain(code, stored, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }
}

public sealed class BotLoginCommandTests
{
    [Fact]
    public async Task EmptyInstallBootstrapsOwnerAndReturnsALoginLink()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();

            var reply = await new BotCommandRouter(path)
                .HandleAsync(new BotMessage(555, 555, "First", "first_user", "/login"));

            Assert.Contains("/auth/bot/", reply);
            var user = await new AppUserRepository(path).GetAsync(555);
            Assert.NotNull(user);
            Assert.Equal(AppUserRole.Owner, user!.Role);
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task LinkCanBeExchangedForTheAccountThatAsked()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var router = new BotCommandRouter(path);

            var reply = await router.HandleAsync(new BotMessage(555, 555, "First", "first_user", "/login"));
            var code = reply[(reply.IndexOf("/auth/bot/", StringComparison.Ordinal) + "/auth/bot/".Length)..].Trim();

            Assert.Equal(555, await new BotLoginCodeRepository(path).ConsumeAsync(code, DateTimeOffset.UtcNow));
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task UnknownUserOnPopulatedInstallGetsNoLink()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await new AppUserRepository(path).UpsertAsync(
                new AppUser(111, "Owner", "User", null, AppUserRole.Owner, false, DateTimeOffset.UtcNow, null));

            var reply = await new BotCommandRouter(path)
                .HandleAsync(new BotMessage(999, 999, "Stranger", null, "/login"));

            Assert.DoesNotContain("/auth/bot/", reply);
            Assert.Contains("ยังไม่ได้รับเชิญ", reply);
            Assert.Null(await new AppUserRepository(path).GetAsync(999));
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task BlockedUserIsRefused()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await new AppUserRepository(path).UpsertAsync(
                new AppUser(222, "Blocked", "User", null, AppUserRole.Owner, true, DateTimeOffset.UtcNow, null));

            var reply = await new BotCommandRouter(path)
                .HandleAsync(new BotMessage(222, 222, "Blocked", null, "/login"));

            Assert.Equal("บัญชีนี้ถูกระงับการใช้งาน", reply);
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }
}
