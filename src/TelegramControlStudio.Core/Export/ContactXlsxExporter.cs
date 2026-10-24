using System.IO.Compression;
using System.Security;
using System.Text;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Export;

public static class ContactXlsxExporter
{
    private static readonly string[] Headers = ["telegram_user_id", "first_name", "last_name", "username", "phone_number", "has_consent", "is_opted_out"];

    public static void Write(Stream destination, IEnumerable<Contact> contacts)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        WriteEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
        WriteEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        WriteEntry(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Contacts\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
        var rows = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        rows.Append(Row(1, Headers.Select(TextCell).ToArray()));
        var rowNumber = 2;
        foreach (var contact in contacts)
        {
            rows.Append(Row(rowNumber++, [NumberCell(contact.TelegramUserId), TextCell(contact.FirstName), TextCell(contact.LastName), TextCell(contact.Username ?? string.Empty), TextCell(contact.PhoneNumber ?? string.Empty), NumberCell(contact.HasConsent ? 1 : 0), NumberCell(contact.IsOptedOut ? 1 : 0)]));
        }
        rows.Append("</sheetData></worksheet>");
        WriteEntry(archive, "xl/worksheets/sheet1.xml", rows.ToString());
    }

    private static string Row(int number, string[] cells) => $"<row r=\"{number}\">{string.Join(string.Empty, cells.Select((cell, index) => cell.Replace("{COL}", Column(index + 1))))}</row>";
    private static string TextCell(string value) => "<c r=\"{COL}\" t=\"inlineStr\"><is><t>" + SecurityElement.Escape(value) + "</t></is></c>";
    private static string NumberCell(long value) => $"<c r=\"{{COL}}\"><v>{value}</v></c>";
    private static string Column(int number) { var result = string.Empty; while (number > 0) { number--; result = (char)('A' + number % 26) + result; number /= 26; } return result; }
    private static void WriteEntry(ZipArchive archive, string path, string content) { using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8); writer.Write(content); }
}

