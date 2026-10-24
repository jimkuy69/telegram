using System.Collections.Concurrent;
using System.Text.Json;
using TelegramControlStudio.Core.Telegram;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibJsonClient : ITdLibClient
{
    private readonly TextWriter _writer;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();
    private long _requestNumber;

    public int PendingRequestCount => _pending.Count;

    public TdLibJsonClient(TextWriter writer) => _writer = writer;

    public async Task<string> SendAsync(
        string function,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var extra = Interlocked.Increment(ref _requestNumber).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[extra] = completion;
        using var registration = cancellationToken.Register(() =>
        {
            _pending.TryRemove(extra, out _);
            completion.TrySetCanceled(cancellationToken);
        });

        try
        {
            var payload = new Dictionary<string, object?>(parameters)
            {
                ["@type"] = function,
                ["@extra"] = extra
            };
            await _writer.WriteLineAsync(JsonSerializer.Serialize(payload));
            await _writer.FlushAsync(cancellationToken);
            return await completion.Task;
        }
        catch
        {
            _pending.TryRemove(extra, out _);
            throw;
        }
    }

    // The native receive loop will call this method for every complete TDLib JSON object.
    public bool HandleResponse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("@extra", out var extraElement))
                return false;

            var extra = extraElement.GetString() ?? extraElement.ToString();
            return _pending.TryRemove(extra, out var completion) && completion.TrySetResult(json);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var pending in _pending.Values)
            pending.TrySetCanceled();
        _pending.Clear();
        return ValueTask.CompletedTask;
    }
}

