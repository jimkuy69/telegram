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
            var preview = ContactCsvImporter.Preview("telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out
42,A,B,,,true,false", 7);
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
            var preview = ContactCsvImporter.Preview("telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out
42,A,B,,,true,false
43,C,D,,,true,false", accountId);
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
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}