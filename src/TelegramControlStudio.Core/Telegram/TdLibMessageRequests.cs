using TelegramControlStudio.Core.Safety;

namespace TelegramControlStudio.Core.Telegram;

public static class TdLibMessageRequests
{
    public static TdLibRequest SendText(
        long chatId,
        string text,
        bool hasConsent,
        bool isOptedOut,
        bool confirmed,
        SendPurpose purpose = SendPurpose.Campaign)
    {
        if (!confirmed) throw new InvalidOperationException("CONFIRMATION_REQUIRED");
        // A consent request is sent before consent exists, so its guard is the request policy at
        // the call site. Campaign sending keeps the hard, unforgeable consent requirement here.
        if (purpose == SendPurpose.Campaign)
        {
            if (!hasConsent) throw new InvalidOperationException("CONSENT_REQUIRED");
            if (isOptedOut) throw new InvalidOperationException("OPT_OUT_RECIPIENT");
        }
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

