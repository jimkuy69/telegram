using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ConsentRequestRepositoryTests
{
    private static readonly DateTimeOffset Base = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);

    private static async Task<(string Path, long AccountId)> NewDatabaseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-consent-requests-{Guid.NewGuid():N}.db");
        await new SqliteDatabase(path).InitializeAsync();
        var accountId = await new AccountRepository(path).CreateAsync("Primary", "+66800000001");
        return (path, accountId);
    }

    private static ConsentRequest Event(
        long accountId,
        long userId,
        string kind,
        DateTimeOffset occurredAt,
        string? bodyHash = null) =>
        new(0, accountId, userId, kind, occurredAt, 5001, 6001, 7, bodyHash, null, ConsentRequestSources.JobRunner);

    [Fact]
    public async Task AppendThenReadBackKeepsEveryEvidenceField()
    {
        var (path, accountId) = await NewDatabaseAsync();
        try
        {
            var repository = new ConsentRequestRepository(path);
            var id = await repository.AppendAsync(new ConsentRequest(
                0, accountId, 42, ConsentRequestKinds.Requested, Base, 9001, 6001, 7, "abc123", null, ConsentRequestSources.JobRunner));
            Assert.True(id > 0);

            var rows = await repository.ListAsync(accountId);
            var row = Assert.Single(rows);
            Assert.Equal(42, row.TelegramUserId);
            Assert.Equal(ConsentRequestKinds.Requested, row.Kind);
            Assert.Equal(Base, row.OccurredAt);
            Assert.Equal(9001, row.MessageId);
            Assert.Equal(6001, row.ChatId);
            Assert.Equal(7, row.TemplateId);
            Assert.Equal("abc123", row.BodySha256);
            Assert.Equal(ConsentRequestSources.JobRunner, row.Source);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task ARequestWithoutTheHashOfWhatWasSentIsRefused()
    {
        var (path, accountId) = await NewDatabaseAsync();
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => new ConsentRequestRepository(path).AppendAsync(Event(accountId, 42, ConsentRequestKinds.Requested, Base)));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task UnknownEventKindsAreRefused()
    {
        var (path, accountId) = await NewDatabaseAsync();
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => new ConsentRequestRepository(path).AppendAsync(Event(accountId, 42, "Maybe", Base, "hash")));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task LastRequestedIgnoresRepliesAndKeepsTheNewestRequest()
    {
        var (path, accountId) = await NewDatabaseAsync();
        try
        {
            var repository = new ConsentRequestRepository(path);
            await repository.AppendAsync(Event(accountId, 42, ConsentRequestKinds.Requested, Base, "hash-one"));
            await repository.AppendAsync(Event(accountId, 42, ConsentRequestKinds.Requested, Base.AddDays(30), "hash-two"));
            await repository.AppendAsync(Event(accountId, 42, ConsentRequestKinds.Granted, Base.AddDays(31)));

            var last = await repository.LastRequestedAtAsync(accountId, 42);
            Assert.Equal(Base.AddDays(30), last);
            Assert.Null(await repository.LastRequestedAtAsync(accountId, 999));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task DailyCountOnlyIncludesRequestsInsideTheWindow()
    {
        var (path, accountId) = await NewDatabaseAsync();
        try
        {
            var repository = new ConsentRequestRepository(path);
            await repository.AppendAsync(Event(accountId, 42, ConsentRequestKinds.Requested, Base, "h1"));
            await repository.AppendAsync(Event(accountId, 43, ConsentRequestKinds.Requested, Base.AddHours(2), "h2"));
            await repository.AppendAsync(Event(accountId, 44, ConsentRequestKinds.Requested, Base.AddDays(-2), "h3"));
            await repository.AppendAsync(Event(accountId, 45, ConsentRequestKinds.Granted, Base.AddHours(3)));

            Assert.Equal(2, await repository.CountRequestedSinceAsync(accountId, Base));
            Assert.Equal(3, await repository.CountRequestedSinceAsync(accountId, Base.AddDays(-7)));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task TheNewestEventOfAContactWinsRegardlessOfKind()
    {
        var (path, accountId) = await NewDatabaseAsync();
        try
        {
            var repository = new ConsentRequestRepository(path);
            await repository.AppendAsync(Event(accountId, 42, ConsentRequestKinds.Requested, Base, "h1"));
            await repository.AppendAsync(Event(accountId, 42, ConsentRequestKinds.Rejected, Base.AddHours(1)));

            var latest = await repository.LatestForContactAsync(accountId, 42);
            Assert.Equal(ConsentRequestKinds.Rejected, latest!.Kind);
            Assert.Null(await repository.LatestForContactAsync(accountId, 999));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task ARequestOnlyMatchesTheChatItWasSentIn()
    {
        var (path, accountId) = await NewDatabaseAsync();
        try
        {
            var repository = new ConsentRequestRepository(path);
            await repository.AppendAsync(new ConsentRequest(
                0, accountId, 42, ConsentRequestKinds.Requested, Base, 9001, 6001, 7, "h1", null, ConsentRequestSources.JobRunner));

            Assert.NotNull(await repository.LatestRequestInChatAsync(accountId, 42, 6001));
            Assert.Null(await repository.LatestRequestInChatAsync(accountId, 42, 7007));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task LatestByContactFollowsTheNewestEventOfEachContact()
    {
        var (path, accountId) = await NewDatabaseAsync();
        try
        {
            var repository = new ConsentRequestRepository(path);
            await repository.AppendAsync(Event(accountId, 42, ConsentRequestKinds.Requested, Base, "h1"));
            await repository.AppendAsync(Event(accountId, 42, ConsentRequestKinds.Granted, Base.AddHours(1), null));
            await repository.AppendAsync(Event(accountId, 43, ConsentRequestKinds.Requested, Base.AddHours(2), "h2"));

            var latest = await repository.LatestByContactAsync(accountId);
            Assert.Equal(2, latest.Count);
            Assert.Equal(ConsentRequestKinds.Granted, latest[42].Kind);
            Assert.Equal(ConsentRequestKinds.Requested, latest[43].Kind);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task SettingsFallBackToTheShippedDefaultsWhenNothingWasStored()
    {
        var (path, _) = await NewDatabaseAsync();
        try
        {
            var settings = await new ConsentRequestSettingsRepository(path).GetAsync();
            Assert.Equal(ConsentRequestSettings.Default, settings);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task SettingsRoundTripAndRejectUnusableRanges()
    {
        var (path, _) = await NewDatabaseAsync();
        try
        {
            var repository = new ConsentRequestSettingsRepository(path);
            await repository.SetAsync(new ConsentRequestSettings(60, 5));
            Assert.Equal(new ConsentRequestSettings(60, 5), await repository.GetAsync());

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.SetAsync(new ConsentRequestSettings(60, 5000)));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.SetAsync(new ConsentRequestSettings(-1, 5)));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task ACorruptedStoredValueDoesNotWidenTheRules()
    {
        var (path, _) = await NewDatabaseAsync();
        try
        {
            await new ConsentRequestSettingsRepository(path).SetAsync(new ConsentRequestSettings(15, 4));
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                           new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = "UPDATE app_settings SET value = 'not-a-number' WHERE key = 'consent_request_daily_limit';";
                await command.ExecuteNonQueryAsync();
            }

            var settings = await new ConsentRequestSettingsRepository(path).GetAsync();
            Assert.Equal(15, settings.CooldownDays);
            Assert.Equal(ConsentRequestSettings.Default.DailyLimit, settings.DailyLimit);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
