using System.Text.Json;

namespace TelegramControlStudio.Core.Telegram;

/// <summary>
/// A text message that arrived from the other side of a chat. Only what a consent answer needs is
/// kept: who spoke, where, which message it was, and whether it answers our request.
/// </summary>
public sealed record InboundTelegramMessage(
    long ChatId,
    long SenderUserId,
    long MessageId,
    long? ReplyToMessageId,
    string Text);

/// <summary>
/// Reads <c>updateNewMessage</c> updates. Nothing else counts: our own echoed copies, non-text
/// content, and updates we cannot attribute to a user are all ignored, because an unanswered
/// question is safer than a wrongly recorded consent.
/// </summary>
public static class ConsentReplyUpdateParser
{
    public static InboundTelegramMessage? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!HasType(root, "updateNewMessage")) return null;
            if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) return null;
            if (TryGetBoolean(message, "is_outgoing")) return null;

            var senderId = TryReadNestedId(message, "sender_id", "user_id");
            var chatId = TryReadNestedId(message, "chat_id", "id");
            var messageId = TryReadId(message, "id");
            if (senderId is null || chatId is null || messageId is null) return null;

            var text = TryReadText(message);
            if (string.IsNullOrWhiteSpace(text)) return null;

            return new InboundTelegramMessage(
                chatId.Value,
                senderId.Value,
                messageId.Value,
                TryReadId(message, "reply_to_message_id"),
                text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadText(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object) return null;
        if (!HasType(content, "messageText")) return null;
        if (!content.TryGetProperty("text", out var wrapper) || wrapper.ValueKind != JsonValueKind.Object) return null;
        return wrapper.TryGetProperty("text", out var inner) && inner.ValueKind == JsonValueKind.String
            ? inner.GetString()
            : null;
    }

    private static bool HasType(JsonElement element, string expected) =>
        element.TryGetProperty("@type", out var type) && string.Equals(type.GetString(), expected, StringComparison.Ordinal);

    private static bool TryGetBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Reads an id that TDLib may deliver either as a number or wrapped in an object, as it does for
    /// <c>chat_id</c> and <c>sender_id</c> in newer builds. The wrapped form names its number
    /// differently per object, so the caller says which field to look for inside it.
    /// </summary>
    private static long? TryReadNestedId(JsonElement element, string property, string innerProperty)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var direct)) return direct;
        return value.ValueKind == JsonValueKind.Object &&
               value.TryGetProperty(innerProperty, out var inner) && inner.TryGetInt64(out var wrapped)
            ? wrapped
            : null;
    }

    private static long? TryReadId(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var parsed)
            ? parsed
            : null;
}
