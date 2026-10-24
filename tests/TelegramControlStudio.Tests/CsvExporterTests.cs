using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Export;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class CsvExporterTests
{
    [Fact]
    public void EscapesCommasAndQuotes()
    {
        var csv = CsvExporter.Contacts([
            new Contact(1, 2, "A, "quoted"", "", null, null, true, false, DateTimeOffset.UtcNow)
        ]);
        Assert.Contains(""A, ""quoted"""", csv);
        Assert.Contains("telegram_user_id", csv);
    }
}
