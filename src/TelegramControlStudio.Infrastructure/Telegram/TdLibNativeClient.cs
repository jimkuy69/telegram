using System.Runtime.InteropServices;
using System.Text;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibNativeClient : IDisposable
{
    private readonly nint _library;
    private readonly TdCreateClientId _createClientId;
    private readonly TdSend _send;
    private readonly TdReceive _receive;
    private readonly TdExecute _execute;
    private bool _disposed;

    public TdLibNativeClient(nint libraryHandle)
    {
        if (libraryHandle == 0) throw new ArgumentOutOfRangeException(nameof(libraryHandle));
        _library = libraryHandle;
        _createClientId = LoadExport<TdCreateClientId>("td_create_client_id");
        _send = LoadExport<TdSend>("td_send");
        _receive = LoadExport<TdReceive>("td_receive");
        _execute = LoadExport<TdExecute>("td_execute");
    }

    public int CreateClientId() => _createClientId();

    public void Send(int clientId, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var bytes = Encoding.UTF8.GetBytes(json + "\0");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            _send(clientId, pointer);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    public string? Receive(double timeoutSeconds)
    {
        var pointer = _receive(timeoutSeconds);
        return pointer == 0 ? null : Marshal.PtrToStringUTF8(pointer);
    }

    public string? Execute(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var bytes = Encoding.UTF8.GetBytes(json + "\0");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var result = _execute(pointer);
            return result == 0 ? null : Marshal.PtrToStringUTF8(result);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private T LoadExport<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    public void Dispose()
    {
        if (_disposed) return;
        NativeLibrary.Free(_library);
        _disposed = true;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TdCreateClientId();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void TdSend(int clientId, nint request);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint TdReceive(double timeoutSeconds);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint TdExecute(nint request);
}

