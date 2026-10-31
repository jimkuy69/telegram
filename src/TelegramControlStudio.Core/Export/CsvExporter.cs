using System.Text;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Export;

public static class CsvExporter
{
    public static string ContactsForImport(IEnumerable<Contact> contacts)
    {
        var builder = new StringBuilder();
        builder.AppendLine("telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out");
        foreach (var contact in contacts)
            builder.AppendLine(string.Join(",", contact.TelegramUserId, Escape(contact.FirstName), Escape(contact.LastName), Escape(contact.Username), Escape(contact.PhoneNumber), contact.HasConsent, contact.IsOptedOut));
        return builder.ToString();
    }

    public static string Contacts(IEnumerable<Contact> contacts)
    {
        var builder = new StringBuilder();
        builder.AppendLine("account_id,telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out,updated_at");
        foreach (var contact in contacts)
            builder.AppendLine(string.Join(",", contact.AccountId, contact.TelegramUserId, Escape(contact.FirstName), Escape(contact.LastName), Escape(contact.Username), Escape(contact.PhoneNumber), contact.HasConsent, contact.IsOptedOut, Escape(contact.UpdatedAt.ToString("O"))));
        return builder.ToString();
    }

    public static string Rows(IEnumerable<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();
        foreach (var row in rows)
            builder.AppendLine(string.Join(",", row.Select(Cell)));
        return builder.ToString();
    }

    private static string Cell(string? value) => value is null ? string.Empty : Escape(value);

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        var quote = ((char)34).ToString();
        return quote + text.Replace(quote, quote + quote) + quote;
    }
}

