using System.Runtime.InteropServices;
using System.Text;

namespace TelegramControlStudio.Infrastructure.Telegram;

/// <summary>
/// Wraps one TDLib client instance. The per-instance <c>td_json_client_*</c> entry points are
/// used rather than the process-global <c>td_receive</c>, because TDLib aborts the whole process
/// as soon as two clients receive updates from different threads at the same time.
/// </summary>
public sealed class TdLibNativeClient : IDisposable
{
    private readonly nint _library;
    private readonly nint _instance;
    private readonly TdSend _send;
    private readonly TdReceive _receive;
    private readonly TdExecute _execute;
    private readonly TdDestroy _destroy;
    private bool _disposed;

    public TdLibNativeClient(nint libraryHandle)
    {
        if (libraryHandle == 0) throw new ArgumentOutOfRangeException(nameof(libraryHandle));
        _library = libraryHandle;
        var create = LoadExport<TdCreate>("td_json_client_create");
        _send = LoadExport<TdSend>("td_json_client_send");
        _receive = LoadExport<TdReceive>("td_json_client_receive");
        _execute = LoadExport<TdExecute>("td_json_client_execute");
        _destroy = LoadExport<TdDestroy>("td_json_client_destroy");

        _instance = create();
        if (_instance == 0)
            throw new InvalidOperationException("TDLib did not create a client instance.");
    }

    public void Send(string json)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        WithUtf8Buffer(json, request => _send(_instance, request));
    }

    /// <remarks>
    /// TDLib binds a client to the thread that receives from it, so callers must keep this on one
    /// dedicated thread for the lifetime of the instance.
    /// </remarks>
    public string? Receive(double timeoutSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var pointer = _receive(_instance, timeoutSeconds);
        return pointer == 0 ? null : Marshal.PtrToStringUTF8(pointer);
    }

    public string? Execute(string json)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        nint result = 0;
        WithUtf8Buffer(json, request => result = _execute(_instance, request));
        return result == 0 ? null : Marshal.PtrToStringUTF8(result);
    }

    private static void WithUtf8Buffer(string json, Action<nint> invoke)
    {
        var bytes = Encoding.UTF8.GetBytes(json + "\0");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            invoke(pointer);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private T LoadExport<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _destroy(_instance);
        NativeLibrary.Free(_library);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint TdCreate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void TdSend(nint client, nint request);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint TdReceive(nint client, double timeoutSeconds);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint TdExecute(nint client, nint request);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void TdDestroy(nint client);
}
