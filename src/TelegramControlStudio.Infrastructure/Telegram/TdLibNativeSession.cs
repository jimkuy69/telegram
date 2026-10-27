using System.Collections.Concurrent;
using System.Text.Json;
using TelegramControlStudio.Core.Telegram;

namespace TelegramControlStudio.Infrastructure.Telegram;

/// <summary>
/// Owns one native TDLib client, its receive loop, request correlation, and update routing.
/// </summary>
public sealed class TdLibNativeSession : ITdLibClient
{
    private readonly TdLibNativeClient _nativeClient;
    private readonly int _clientId;
    private readonly TdLibUpdateRouter _router;
    private readonly Action<string>? _onUnmatchedUpdate;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _receiveLoop;
    private long _requestNumber;
    private int _disposed;

    private TdLibNativeSession(
        TdLibNativeClient nativeClient,
        int clientId,
        TdLibUpdateRouter router,
        Action<string>? onUnmatchedUpdate)
    {
        _nativeClient = nativeClient;
        _clientId = clientId;
        _router = router;
        _onUnmatchedUpdate = onUnmatchedUpdate;
        _receiveLoop = Task.Run(ReceiveLoopAsync);
    }

    public static async Task<TdLibNativeSession> CreateAsync(
        string nativeDirectory,
        string expectedSha256,
        IReadOnlyDictionary<string, object?> parameters,
        TdLibUpdateRouter router,
        Action<string>? onUnmatchedUpdate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(router);

        var loader = new TdLibNativeLoader();
        var handle = loader.Load(nativeDirectory, expectedSha256);
        var nativeClient = new TdLibNativeClient(handle);
        try
        {
            var clientId = nativeClient.CreateClientId();
            if (clientId <= 0)
                throw new InvalidOperationException("TDLib did not return a valid client id.");

            var session = new TdLibNativeSession(nativeClient, clientId, router, onUnmatchedUpdate);
            var parameterResponse = await session.SendAsync("setTdlibParameters", parameters, cancellationToken).ConfigureAwait(false);
            EnsureSuccessfulResponse(parameterResponse, "setTdlibParameters");
            return session;
        }
        catch
        {
            nativeClient.Dispose();
            throw;
        }
    }

    public async Task<string> SendAsync(
        string function,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(function);
        ArgumentNullException.ThrowIfNull(parameters);

        var extra = Interlocked.Increment(ref _requestNumber)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pending.TryAdd(extra, completion))
            throw new InvalidOperationException("TDLib request correlation key collision.");

        using var registration = cancellationToken.Register(() =>
        {
            if (_pending.TryRemove(extra, out var pending))
                pending.TrySetCanceled(cancellationToken);
        });

        try
        {
            var payload = new Dictionary<string, object?>(parameters)
            {
                ["@type"] = function,
                ["@extra"] = extra
            };
            _nativeClient.Send(_clientId, JsonSerializer.Serialize(payload));
            return await completion.Task.ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(extra, out _);
            throw;
        }
    }

    private static void EnsureSuccessfulResponse(string response, string operation)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            if (root.TryGetProperty("@type", out var type) &&
                string.Equals(type.GetString(), "error", StringComparison.Ordinal))
            {
                var code = root.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : "unknown";
                var message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : "unknown";
                throw new InvalidOperationException($"TDLIB_{operation.ToUpperInvariant()}_FAILED_{code}_{message}");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"TDLIB_{operation.ToUpperInvariant()}_INVALID_RESPONSE", exception);
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var json = _nativeClient.Receive(1.0);
                if (string.IsNullOrWhiteSpace(json))
                    continue;

                if (!TryCompleteRequest(json))
                {
                    _router.Route(json);
                    _onUnmatchedUpdate?.Invoke(json);
                }

                await Task.Yield();
            }
        }
        catch (Exception exception) when (
            exception is ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            foreach (var pending in _pending.Values)
                pending.TrySetException(exception);
            _pending.Clear();
        }
    }

    private bool TryCompleteRequest(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("@extra", out var extraElement))
                return false;

            var extra = extraElement.ValueKind == JsonValueKind.String
                ? extraElement.GetString()
                : extraElement.ToString();

            return !string.IsNullOrWhiteSpace(extra) &&
                   _pending.TryRemove(extra, out var completion) &&
                   completion.TrySetResult(json);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _shutdown.Cancel();
        foreach (var pending in _pending.Values)
            pending.TrySetCanceled();
        _pending.Clear();

        try
        {
            await _receiveLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }

        _shutdown.Dispose();
        _nativeClient.Dispose();
    }
}
