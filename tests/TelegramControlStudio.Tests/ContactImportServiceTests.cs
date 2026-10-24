using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Import;
using TelegramControlStudio.Infrastructure.Import;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ContactImportServiceTests
{
    [Fact]
    public async Task RequiresConfirmationBeforeWriting()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telegram-import-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var preview = ContactCsvImporter.Preview("telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out\n42,A,B,,,true,false", 7);
            var result = await new ContactImportService(path).CommitAsync(preview, false);
            Assert.Equal(0, result.Imported);
            Assert.Contains("CONFIRMATION_REQUIRED", result.Errors);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ConfirmedImportWritesThePreviewAsOneBatch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telegram-import-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Import", "+66000000005");
            var preview = ContactCsvImporter.Preview("telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out\n42,A,B,,,true,false\n43,C,D,,,true,false", accountId);
            var result = await new ContactImportService(path).CommitAsync(preview, true);
            Assert.Equal(2, result.Imported);
            Assert.Equal(2, (await new ContactRepository(path).ListAsync(accountId)).Count);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task SoftDeleteHidesContactButKeepsAuditRow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telegram-import-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Delete", "+66000000006");
            await new ContactRepository(path).UpsertAsync(new TelegramControlStudio.Core.Domain.Contact(accountId, 99, "A", "B", null, null, true, false, DateTimeOffset.UtcNow));

            Assert.True(await new ContactRepository(path).SoftDeleteAsync(accountId, 99));
            Assert.False(await new ContactRepository(path).SoftDeleteAsync(accountId, 99));
            Assert.Empty(await new ContactRepository(path).ListAsync(accountId));

            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM contacts WHERE account_id = $account_id AND telegram_user_id = $user_id AND deleted_at IS NOT NULL";
            command.Parameters.AddWithValue("$account_id", accountId);
            command.Parameters.AddWithValue("$user_id", 99);
            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task SearchFiltersByNameUsernamePhoneAndAccount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telegram-import-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Search", "+66000000007");
            var otherAccountId = await new AccountRepository(path).CreateAsync("Other", "+66000000008");
            var repository = new ContactRepository(path);
            await repository.UpsertManyAsync([
                new TelegramControlStudio.Core.Domain.Contact(accountId, 100, "Alice", "One", "alice", "+66810000000", true, false, DateTimeOffset.UtcNow),
                new TelegramControlStudio.Core.Domain.Contact(accountId, 101, "Bob", "Two", "bobby", "+66820000000", false, false, DateTimeOffset.UtcNow),
                new TelegramControlStudio.Core.Domain.Contact(otherAccountId, 100, "Alice", "Other", "alice-other", null, true, false, DateTimeOffset.UtcNow)]);

            Assert.Single(await repository.SearchAsync(accountId, "bobby"));
            Assert.Single(await repository.SearchAsync(accountId, "66810000000"));
            Assert.Equal(2, (await repository.SearchAsync(accountId, null)).Count);
            Assert.DoesNotContain(await repository.SearchAsync(accountId, "Alice"), item => item.AccountId != accountId);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ConsentAndOptOutPoliciesAreAccountScoped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telegram-import-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Policy", "+66000000011");
            var otherAccountId = await new AccountRepository(path).CreateAsync("Other", "+66000000012");
            var repository = new ContactRepository(path);
            await repository.UpsertManyAsync([
                new TelegramControlStudio.Core.Domain.Contact(accountId, 501, "A", "", null, null, false, false, DateTimeOffset.UtcNow),
                new TelegramControlStudio.Core.Domain.Contact(otherAccountId, 501, "B", "", null, null, false, false, DateTimeOffset.UtcNow)]);

            Assert.True(await repository.SetConsentAsync(accountId, 501, true));
            Assert.True(await repository.SetOptOutAsync(accountId, 501, true));
            Assert.False(await repository.SetConsentAsync(accountId, 999, true));
            var contact = (await repository.ListAsync(accountId)).Single();
            Assert.True(contact.HasConsent);
            Assert.True(contact.IsOptedOut);
            Assert.False((await repository.ListAsync(otherAccountId)).Single().HasConsent);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}