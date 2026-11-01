using System.Collections.Concurrent;
using System.Text.Json;
using TelegramControlStudio.Core.Telegram;

namespace TelegramControlStudio.Infrastructure.Telegram;

/// <summary>
/// Owns one native TDLib client, its receive loop, request correlation, and update routing.
/// TDLib binds a client to the thread that receives from it, so the receive loop runs on one
/// dedicated thread for the lifetime of the session.
/// </summary>
public sealed class TdLibNativeSession : ITdLibClient
{
    private const string CloseExtra = "tcs-session-close";
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReceiveThreadJoinTimeout = TimeSpan.FromSeconds(10);
    private readonly TdLibNativeClient _nativeClient;
    private readonly TdLibUpdateRouter _router;
    private readonly Action<string>? _onUnmatchedUpdate;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource<bool> _receiveThreadReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _receiveThread;
    private long _requestNumber;
    private int _disposed;

    private TdLibNativeSession(
        TdLibNativeClient nativeClient,
        TdLibUpdateRouter router,
        Action<string>? onUnmatchedUpdate)
    {
        _nativeClient = nativeClient;
        _router = router;
        _onUnmatchedUpdate = onUnmatchedUpdate;
        _receiveThread = new Thread(ReceiveThreadMain)
        {
            IsBackground = true,
            Name = "TCS-TDLib-Receive",
        };
        _receiveThread.Start();
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
        var session = new TdLibNativeSession(nativeClient, router, onUnmatchedUpdate);
        try
        {
            await session._receiveThreadReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            var parameterResponse = await session.SendAsync("setTdlibParameters", parameters, cancellationToken).ConfigureAwait(false);
            EnsureSuccessfulResponse(parameterResponse, "setTdlibParameters");
            return session;
        }
        catch
        {
            // The receive thread keeps calling into the library, so it has to stop before the
            // client instance and the library handle are released.
            await session.ShutdownAsync().ConfigureAwait(false);
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
            // td_json_client_send is documented as callable from any thread; only receiving is
            // bound to one thread.
            _nativeClient.Send(JsonSerializer.Serialize(payload));
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

    private void ReceiveThreadMain()
    {
        try
        {
            _receiveThreadReady.TrySetResult(true);
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
            }
        }
        catch (Exception exception) when (
            exception is ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            _receiveThreadReady.TrySetException(exception);
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

        await ShutdownAsync().ConfigureAwait(false);
    }

    private async Task ShutdownAsync()
    {
        await RequestTdLibCloseAsync().ConfigureAwait(false);

        _shutdown.Cancel();
        foreach (var pending in _pending.Values)
            pending.TrySetCanceled();
        _pending.Clear();

        // Destroying the instance while its receive call is still running would be undefined
        // behaviour, so the thread has to be gone first; a stuck thread leaks the client instead.
        if (_receiveThread.Join(ReceiveThreadJoinTimeout))
            _nativeClient.Dispose();

        _shutdown.Dispose();
    }

    // TDLib frees the account's session directory only after `close` completes, so skipping it
    // leaves the next connect of the same account failing on a locked database.
    private async Task RequestTdLibCloseAsync()
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(CloseExtra, completion))
            return;

        using var timeout = new CancellationTokenSource(CloseTimeout);
        using var registration = timeout.Token.Register(() =>
        {
            if (_pending.TryRemove(CloseExtra, out var pending))
                pending.TrySetCanceled();
        });

        try
        {
            _nativeClient.Send($$"""{"@type":"close","@extra":"{{CloseExtra}}"}""");
            await completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
}
