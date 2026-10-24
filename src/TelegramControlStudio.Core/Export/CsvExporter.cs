using System.Text;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Export;

public static class CsvExporter
{
    public static string Contacts(IEnumerable<Contact> contacts)
    {
        var builder = new StringBuilder();
        builder.AppendLine("account_id,telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out,updated_at");
        foreach (var contact in contacts)
        {
            builder.AppendLine(string.Join(",",
                contact.AccountId,
                contact.TelegramUserId,
                Escape(contact.FirstName),
                Escape(contact.LastName),
                Escape(contact.Username),
                Escape(contact.PhoneNumber),
                contact.HasConsent,
                contact.IsOptedOut,
                Escape(contact.UpdatedAt.ToString("O"))));
        }
        return builder.ToString();
    }

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        return $""{text.Replace(""", """")}"";
    }
}
