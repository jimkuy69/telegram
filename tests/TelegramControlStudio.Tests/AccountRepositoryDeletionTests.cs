using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class AccountRepositoryDeletionTests
{
    private static async Task<(string Path, long Keep, long Remove)> NewDatabaseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-account-delete-{Guid.NewGuid():N}.db");
        await new SqliteDatabase(path).InitializeAsync();
        var repository = new AccountRepository(path);
        var keep = await repository.CreateAsync("Keep", "+66800000001");
        var remove = await repository.CreateAsync("Junk", "+66800000002");
        await SeedAsync(path, keep);
        await SeedAsync(path, remove);
        return (path, keep, remove);
    }

    /// <summary>One row in every table an account owns, so a missed table shows up as a leftover.</summary>
    private static async Task SeedAsync(string path, long accountId)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO contacts(account_id, telegram_user_id, first_name, has_consent, is_opted_out, updated_at)
                VALUES ($a, $user, 'Person', 1, 0, '2026-10-31T00:00:00.0000000+00:00');
            INSERT INTO telegram_groups(account_id, chat_id, title, kind, last_synced_at)
                VALUES ($a, $chat, 'Group', 'Channel', '2026-10-31T00:00:00.0000000+00:00');
            INSERT INTO group_members(account_id, chat_id, telegram_user_id, display_name, synced_at)
                VALUES ($a, $chat, $user, 'Person', '2026-10-31T00:00:00.0000000+00:00');
            INSERT INTO jobs(id, account_id, target_type, target_count, status, retry_count, created_at, idempotency_key)
                VALUES ($job, $a, 'Campaign:consented', 1, 'Completed', 0, '2026-10-31T00:00:00.0000000+00:00', $jobKey);
            INSERT INTO job_items(job_id, account_id, target_id, status, retry_count, idempotency_key, updated_at)
                VALUES ($job, $a, $user, 'Completed', 0, $itemKey, '2026-10-31T00:00:00.0000000+00:00');
            INSERT INTO consent_requests(account_id, telegram_user_id, kind, occurred_at, source)
                VALUES ($a, $user, 'Granted', '2026-10-31T00:00:00.0000000+00:00', 'operator-ui');
            INSERT INTO invite_links(account_id, chat_id, invite_url, created_at)
                VALUES ($a, $chat, $url, '2026-10-31T00:00:00.0000000+00:00');
            INSERT INTO join_requests(account_id, chat_id, telegram_user_id, status, requested_at)
                VALUES ($a, $chat, $user, 'Pending', '2026-10-31T00:00:00.0000000+00:00');
            INSERT INTO activity_logs(account_id, job_id, level, event_type, message, created_at)
                VALUES ($a, $job, 'Info', 'Seeded', 'seed', '2026-10-31T00:00:00.0000000+00:00');
            INSERT INTO account_secrets(account_id, secret_name, protected_value, updated_at)
                VALUES ($a, 'tdlib.api_id', X'01020304', '2026-10-31T00:00:00.0000000+00:00');
            INSERT INTO account_sessions(account_id, session_directory, authorization_state, is_authorized, updated_at)
                VALUES ($a, 'C:/sessions/seed', 'Ready', 1, '2026-10-31T00:00:00.0000000+00:00');
            """;
        command.Parameters.AddWithValue("$a", accountId);
        command.Parameters.AddWithValue("$user", accountId * 1000 + 1);
        command.Parameters.AddWithValue("$chat", accountId * 1000 + 2);
        command.Parameters.AddWithValue("$job", $"job-{accountId}");
        command.Parameters.AddWithValue("$jobKey", $"seed-job-{accountId}");
        command.Parameters.AddWithValue("$itemKey", $"seed-item-{accountId}");
        command.Parameters.AddWithValue("$url", $"https://t.me/seed{accountId}");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Dictionary<string, int>> OwnedCountsAsync(string path, long accountId)
    {
        var counts = new Dictionary<string, int>();
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await connection.OpenAsync();
        foreach (var table in new[]
        {
            "contacts", "telegram_groups", "group_members", "jobs", "job_items", "consent_requests",
            "invite_links", "join_requests", "activity_logs", "account_secrets", "account_sessions"
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE account_id = $a;";
            command.Parameters.AddWithValue("$a", accountId);
            counts[table] = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
        }

        return counts;
    }

    [Fact]
    public async Task DeleteRemovesEveryOwnedRowAndLeavesTheOtherAccountAlone()
    {
        var (path, keep, remove) = await NewDatabaseAsync();
        try
        {
            var repository = new AccountRepository(path);
            var deletion = await repository.DeleteAsync(remove);

            Assert.NotNull(deletion);
            Assert.Equal(remove, deletion!.AccountId);
            Assert.Equal("Junk", deletion.DisplayName);
            Assert.Equal(16, deletion.DeletedRows.Count);
            Assert.Equal(11, deletion.TotalRows);
            Assert.All(await OwnedCountsAsync(path, remove), pair => Assert.Equal(0, pair.Value));
            Assert.All(await OwnedCountsAsync(path, keep), pair => Assert.Equal(1, pair.Value));
            Assert.Equal(new[] { keep }, (await repository.ListAsync()).Select(account => account.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CountOwnedRowsListsWhatTheOperatorIsAboutToLose()
    {
        var (path, _, remove) = await NewDatabaseAsync();
        try
        {
            var repository = new AccountRepository(path);
            var counts = await repository.CountOwnedRowsAsync(remove);

            Assert.Equal(1, counts["contacts"]);
            Assert.Equal(1, counts["jobs"]);
            Assert.Equal(1, counts["job_items"]);
            Assert.Equal(1, counts["consent_requests"]);
            Assert.Equal(1, counts["account_secrets"]);
            Assert.Equal(0, counts["campaigns"]);
            Assert.Equal(0, counts["media_files"]);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DeleteOfAnUnknownAccountReturnsNullWithoutTouchingAnything()
    {
        var (path, keep, _) = await NewDatabaseAsync();
        try
        {
            Assert.Null(await new AccountRepository(path).DeleteAsync(9999));
            Assert.Contains(keep, (await new AccountRepository(path).ListAsync()).Select(account => account.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
