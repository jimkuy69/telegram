using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Export;
using TelegramControlStudio.Core.Import;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ContactXlsxExporterTests
{
    [Fact]
    public void ExportCanBeImportedAgain()
    {
        using var stream = new MemoryStream();
        ContactXlsxExporter.Write(stream, [new Contact(7, 42, "Smith, Jr.", "Family", "smith", "+66812345678", true, false, DateTimeOffset.UtcNow)]);
        stream.Position = 0;
        var result = ContactXlsxImporter.Preview(stream, 7);
        Assert.Single(result.Valid);
        Assert.Empty(result.Errors);
        Assert.Equal("Smith, Jr.", result.Valid[0].FirstName);
    }
}

