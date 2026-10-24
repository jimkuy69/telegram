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
}
