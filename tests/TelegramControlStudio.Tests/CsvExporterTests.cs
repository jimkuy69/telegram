using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Export;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class CsvExporterTests
{
    [Fact]
    public void EscapesCommasAndQuotes()
    {
        var quote = ((char)34).ToString();
        var csv = CsvExporter.Contacts([
            new Contact(1, 2, "A, " + quote + "quoted" + quote, "", null, null, true, false, DateTimeOffset.UtcNow)
        ]);
        Assert.Contains(quote + "A, " + quote + quote + "quoted" + quote + quote + quote, csv);
        Assert.Contains("telegram_user_id", csv);
    }

    [Fact]
    public void ImportCompatibleExportCanBePreviewedAgain()
    {
        var contacts = new[] { new Contact(7, 42, "Smith, Jr.", "Family", "@smith", "+66812345678", true, false, DateTimeOffset.UtcNow) };
        var csv = CsvExporter.ContactsForImport(contacts);
        var result = TelegramControlStudio.Core.Import.ContactCsvImporter.Preview(csv, 7);
        Assert.Single(result.Valid);
        Assert.Empty(result.Errors);
        Assert.Equal("Smith, Jr.", result.Valid[0].FirstName);
        Assert.Equal("smith", result.Valid[0].Username);
    }
}

