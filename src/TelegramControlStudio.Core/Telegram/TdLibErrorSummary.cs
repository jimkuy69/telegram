using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TelegramControlStudio.Core.Telegram;

/// <summary>
/// Turns a TDLib error envelope into a short, log-safe reason such as
/// "403: USER_PRIVACY_RESTRICTED" so the queue and the report can say why Telegram
/// rejected a send. Only the error code and description are kept; the rest of the
/// response never reaches the activity log.
/// </summary>
public static partial class TdLibErrorSummary
{
    public const string SendFailedCode = "TDLIB_MESSAGE_SEND_FAILED";

    public static string Describe(string? response, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(response)) return "0: UNKNOWN";

        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            var code = root.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var value)
                ? value.ToString(CultureInfo.InvariantCulture)
                : "0";
            var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? Whitespace().Replace(messageElement.GetString() ?? string.Empty, " ").Trim()
                : string.Empty;
            if (message.Length > maxLength) message = message[..maxLength];
            return message.Length == 0 ? $"{code}: UNKNOWN" : $"{code}: {message}";
        }
        catch (JsonException)
        {
            return "0: UNKNOWN";
        }
    }

    public static bool IsSendFailure(string? reason) =>
        reason is not null && reason.StartsWith(SendFailedCode, StringComparison.Ordinal);

    public static string? SendFailureDetail(string? reason)
    {
        if (!IsSendFailure(reason)) return null;
        var detail = reason![SendFailedCode.Length..].TrimStart(' ', ':');
        return detail.Length == 0 ? null : detail;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
