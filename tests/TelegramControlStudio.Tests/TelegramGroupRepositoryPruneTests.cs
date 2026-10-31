using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TelegramGroupRepositoryPruneTests
{
    private static readonly DateTimeOffset Base = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);

    private static async Task<(string Path, long AccountId, long OtherAccountId)> NewDatabaseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-group-prune-{Guid.NewGuid():N}.db");
        await new SqliteDatabase(path).InitializeAsync();
        var accounts = new AccountRepository(path);
        var accountId = await accounts.CreateAsync("Primary", "+66800000001");
        var otherAccountId = await accounts.CreateAsync("Secondary", "+66800000002");
        return (path, accountId, otherAccountId);
    }

    private static async Task ExecAsync(string path, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Foreign Keys=True");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private static Task AddGroupAsync(string path, long accountId, long chatId, string title, DateTimeOffset? deletedAt = null) =>
        ExecAsync(
            path,
            "INSERT INTO telegram_groups(account_id, chat_id, title, kind, can_view_members, can_send_messages, can_invite_users, last_synced_at, observed_note, deleted_at)" +
            " VALUES($a, $chat, $title, 'Channel', 0, 0, 0, $synced, '', $deleted);",
            ("$a", accountId), ("$chat", chatId), ("$title", title), ("$synced", Base.ToString("O")), ("$deleted", (object?)deletedAt?.ToString("O") ?? DBNull.Value));

    private static Task AddMemberAsync(string path, long accountId, long chatId, long userId, string name) =>
        ExecAsync(
            path,
            "INSERT INTO group_members(account_id, chat_id, telegram_user_id, display_name, status, synced_at) VALUES($a, $chat, $user, $name, 'observed', $synced);",
            ("$a", accountId), ("$chat", chatId), ("$user", userId), ("$name", name), ("$synced", Base.ToString("O")));

    private static async Task<long> CountAsync(string path, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Foreign Keys=True");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    [Fact]
    public async Task PruneKeepsTheChatsThatSurvivedAndHidesTheRest()
    {
        var (path, accountId, otherAccountId) = await NewDatabaseAsync();
        try
        {
            await AddGroupAsync(path, accountId, 100, "Kept");
            await AddGroupAsync(path, accountId, 200, "Left");
            await AddGroupAsync(path, accountId, 300, "Also left");
            await AddGroupAsync(path, accountId, 400, "Already hidden", deletedAt: Base);
            await AddGroupAsync(path, otherAccountId, 500, "Other account");
            await AddMemberAsync(path, accountId, 100, 9001, "Kept Member");
            await AddMemberAsync(path, accountId, 200, 9002, "Left Member");
            await AddMemberAsync(path, otherAccountId, 500, 9003, "Other Member");

            var pruned = await new TelegramGroupRepository(path).PruneAsync(accountId, [100]);

            Assert.Equal(2, pruned);
            var visible = await new TelegramGroupRepository(path).ListAsync(accountId);
            var kept = Assert.Single(visible);
            Assert.Equal(100, kept.ChatId);
            Assert.Equal(1L, await CountAsync(path, "SELECT COUNT(*) FROM group_members WHERE account_id = $a AND chat_id = 100;", ("$a", accountId)));
            Assert.Equal(0L, await CountAsync(path, "SELECT COUNT(*) FROM group_members WHERE account_id = $a AND chat_id IN (200, 300);", ("$a", accountId)));
            Assert.Equal(1L, await CountAsync(path, "SELECT COUNT(*) FROM telegram_groups WHERE account_id = $a AND deleted_at IS NULL;", ("$a", otherAccountId)));
            Assert.Equal(1L, await CountAsync(path, "SELECT COUNT(*) FROM group_members WHERE account_id = $a;", ("$a", otherAccountId)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PruneWithNoSurvivorsHidesEveryGroupOfThatAccountOnly()
    {
        var (path, accountId, otherAccountId) = await NewDatabaseAsync();
        try
        {
            await AddGroupAsync(path, accountId, 100, "One");
            await AddGroupAsync(path, accountId, 200, "Two");
            await AddGroupAsync(path, otherAccountId, 500, "Other account");
            await AddMemberAsync(path, accountId, 100, 9001, "Member");
            await AddMemberAsync(path, otherAccountId, 500, 9003, "Other Member");

            var pruned = await new TelegramGroupRepository(path).PruneAsync(accountId, []);

            Assert.Equal(2, pruned);
            Assert.Empty(await new TelegramGroupRepository(path).ListAsync(accountId));
            Assert.Equal(0L, await CountAsync(path, "SELECT COUNT(*) FROM group_members WHERE account_id = $a;", ("$a", accountId)));
            Assert.Single(await new TelegramGroupRepository(path).ListAsync(otherAccountId));
            Assert.Equal(1L, await CountAsync(path, "SELECT COUNT(*) FROM group_members WHERE account_id = $a;", ("$a", otherAccountId)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PruneTwiceDoesNotTouchRowsItAlreadyHid()
    {
        var (path, accountId, _) = await NewDatabaseAsync();
        try
        {
            await AddGroupAsync(path, accountId, 100, "Kept");
            await AddGroupAsync(path, accountId, 200, "Left");
            var repository = new TelegramGroupRepository(path);

            Assert.Equal(1, await repository.PruneAsync(accountId, [100]));
            Assert.Equal(0, await repository.PruneAsync(accountId, [100]));
            Assert.Single(await repository.ListAsync(accountId));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
