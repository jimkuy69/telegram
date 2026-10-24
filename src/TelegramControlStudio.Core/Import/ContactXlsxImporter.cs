using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace TelegramControlStudio.Core.Import;

public static class ContactXlsxImporter
{
    public static ContactImportResult Preview(Stream xlsx, long accountId)
    {
        ArgumentNullException.ThrowIfNull(xlsx);
        if (!xlsx.CanRead) throw new ArgumentException("The Excel stream must be readable.", nameof(xlsx));
        using var archive = new ZipArchive(xlsx, ZipArchiveMode.Read, leaveOpen: true);
        var shared = ReadSharedStrings(archive);
        var sheet = archive.GetEntry("xl/worksheets/sheet1.xml") ?? throw new InvalidDataException("WORKSHEET_NOT_FOUND");
        using var stream = sheet.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = new List<SortedDictionary<int, string>>();
        foreach (var row in document.Descendants(ns + "row"))
        {
            var values = new SortedDictionary<int, string>();
            foreach (var cell in row.Elements(ns + "c"))
            {
                var reference = (string?)cell.Attribute("r");
                if (reference is null) continue;
                var column = ColumnNumber(reference);
                var value = cell.Element(ns + "v")?.Value ?? cell.Element(ns + "is")?.Element(ns + "t")?.Value ?? string.Empty;
                if ((string?)cell.Attribute("t") == "s" && int.TryParse(value, out var index) && index >= 0 && index < shared.Count)
                    value = shared[index];
                values[column] = value;
            }
            rows.Add(values);
        }
        if (rows.Count == 0) return new([], ["HEADER_INVALID"]);
        var csv = new StringBuilder();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var max = row.Count == 0 ? 0 : row.Keys.Max();
            var values = Enumerable.Range(1, max).Select(column => row.GetValueOrDefault(column, string.Empty));
            csv.AppendLine(rowIndex == 0 ? string.Join(",", values) : string.Join(",", values.Select(Escape)));
        }
        return ContactCsvImporter.Preview(csv.ToString(), accountId);
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var stream = entry.Open();
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return XDocument.Load(stream).Descendants(ns + "si").Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value))).ToList();
    }

    private static int ColumnNumber(string reference)
    {
        var number = 0;
        foreach (var character in reference.TakeWhile(char.IsLetter))
            number = number * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        return number;
    }

    private static string Escape(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}

