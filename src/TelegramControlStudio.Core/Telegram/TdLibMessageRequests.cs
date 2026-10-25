namespace TelegramControlStudio.Core.Telegram;

public static class TdLibMessageRequests
{
    public static TdLibRequest SendText(long chatId, string text, bool hasConsent, bool isOptedOut, bool confirmed)
    {
        if (!confirmed) throw new InvalidOperationException("CONFIRMATION_REQUIRED");
        if (!hasConsent) throw new InvalidOperationException("CONSENT_REQUIRED");
        if (isOptedOut) throw new InvalidOperationException("OPT_OUT_RECIPIENT");
        if (chatId == 0) throw new ArgumentOutOfRangeException(nameof(chatId));
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new TdLibRequest("sendMessage", new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["input_message_content"] = new Dictionary<string, object?>
            {
                ["@type"] = "inputMessageText",
                ["text"] = new Dictionary<string, object?>
                {
                    ["@type"] = "formattedText", ["text"] = text, ["entities"] = Array.Empty<object>()
                },
                ["clear_draft"] = true,
                ["disable_web_page_preview"] = false
            }
        }, $"send-text-{chatId}");
    }
}

