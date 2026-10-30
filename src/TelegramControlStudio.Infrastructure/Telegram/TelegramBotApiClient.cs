using System.Net.Http.Json;
using System.Text.Json;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed record BotIdentity(long Id, string Username, string FirstName);

public sealed record BotMessage(long ChatId, long FromId, string FromFirstName, string? FromUsername, string Text);

public sealed record BotUpdate(long UpdateId, BotMessage? Message);

/// <summary>Thin Telegram Bot API client — only the calls the web host needs.</summary>
public sealed class TelegramBotApiClient(HttpClient http)
{
    public async Task<BotIdentity> GetMeAsync(string token, CancellationToken cancellationToken = default)
    {
        var root = await SendAsync("getMe", token, null, cancellationToken);
        var result = root.GetProperty("result");
        return new BotIdentity(
            result.GetProperty("id").GetInt64(),
            result.TryGetProperty("username", out var username) ? username.GetString() ?? string.Empty : string.Empty,
            result.TryGetProperty("first_name", out var first) ? first.GetString() ?? string.Empty : string.Empty);
    }

    public async Task<IReadOnlyList<BotUpdate>> GetUpdatesAsync(string token, long offset, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, string>
        {
            ["offset"] = offset.ToString(),
            ["timeout"] = timeoutSeconds.ToString(),
            ["allowed_updates"] = "[\"message\"]"
        };
        var root = await SendAsync("getUpdates", token, payload, cancellationToken);
        var updates = new List<BotUpdate>();
        foreach (var item in root.GetProperty("result").EnumerateArray())
        {
            var updateId = item.GetProperty("update_id").GetInt64();
            BotMessage? message = null;
            if (item.TryGetProperty("message", out var raw) && raw.TryGetProperty("text", out var textNode))
            {
                var from = raw.GetProperty("from");
                message = new BotMessage(
                    raw.GetProperty("chat").GetProperty("id").GetInt64(),
                    from.GetProperty("id").GetInt64(),
                    from.TryGetProperty("first_name", out var first) ? first.GetString() ?? string.Empty : string.Empty,
                    from.TryGetProperty("username", out var username) ? username.GetString() : null,
                    textNode.GetString() ?? string.Empty);
            }
            updates.Add(new BotUpdate(updateId, message));
        }
        return updates;
    }

    public async Task SendMessageAsync(string token, long chatId, string text, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, string>
        {
            ["chat_id"] = chatId.ToString(),
            ["text"] = text.Length > 4000 ? text[..4000] : text,
            ["disable_web_page_preview"] = "true"
        };
        await SendAsync("sendMessage", token, payload, cancellationToken);
    }

    private async Task<JsonElement> SendAsync(string method, string token, Dictionary<string, string>? payload, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.telegram.org/bot{token}/{method}")
        {
            Content = new FormUrlEncodedContent(payload ?? new Dictionary<string, string>())
        };
        using var response = await http.SendAsync(request, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement.Clone();
        if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            var description = root.TryGetProperty("description", out var error) ? error.GetString() : "unknown";
            throw new BotApiException(method, (int)response.StatusCode, description ?? "unknown");
        }
        return root;
    }
}

public sealed class BotApiException(string method, int statusCode, string description)
    : Exception($"Telegram {method} failed ({statusCode}): {description}")
{
    public int StatusCode { get; } = statusCode;
}
