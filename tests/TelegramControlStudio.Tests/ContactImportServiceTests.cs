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
}

