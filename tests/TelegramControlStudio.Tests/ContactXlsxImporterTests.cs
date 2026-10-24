using System.IO.Compression;
using System.Text;
using TelegramControlStudio.Core.Import;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ContactXlsxImporterTests
{
    [Fact]
    public void ImportsFirstWorksheetWithSharedStrings()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "xl/sharedStrings.xml", "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>telegram_user_id</t></si><si><t>first_name</t></si><si><t>last_name</t></si><si><t>username</t></si><si><t>phone_number</t></si><si><t>has_consent</t></si><si><t>is_opted_out</t></si><si><t>Smith, Jr.</t></si><si><t>Family</t></si><si><t>user</t></si></sst>");
            Write(archive, "xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"C1\" t=\"s\"><v>2</v></c><c r=\"D1\" t=\"s\"><v>3</v></c><c r=\"E1\" t=\"s\"><v>4</v></c><c r=\"F1\" t=\"s\"><v>5</v></c><c r=\"G1\" t=\"s\"><v>6</v></c></row><row r=\"2\"><c r=\"A2\"><v>42</v></c><c r=\"B2\" t=\"s\"><v>7</v></c><c r=\"C2\" t=\"s\"><v>8</v></c><c r=\"D2\" t=\"s\"><v>9</v></c><c r=\"F2\"><v>1</v></c><c r=\"G2\"><v>0</v></c></row></sheetData></worksheet>");
        }
        stream.Position = 0;
        var result = ContactXlsxImporter.Preview(stream, 7);
        Assert.Single(result.Valid);
        Assert.Equal("Smith, Jr.", result.Valid[0].FirstName);
        Assert.Equal("user", result.Valid[0].Username);
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
        writer.Write(content);
    }
}

