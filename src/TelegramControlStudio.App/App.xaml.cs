using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using TelegramControlStudio.Infrastructure.Hosting;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.App;

public partial class App : Application
{
    private StudioHost? _host;
    private readonly ConcurrentDictionary<long, TdLibSessionLock> _sessionLocks = new();

    public StudioHost Host => _host ?? throw new InvalidOperationException("Studio host has not started.");

    public TdLibAccountClientRegistry TdLibClients => Host.TdLibClients;

    /// <summary>
    /// Holds the cross-process TDLib session lock for an account. Fails when the web
    /// host owns the session (web sending enabled); the caller must tell the user to
    /// turn it off on the web settings page first.
    /// </summary>
    public bool TryHoldSessionLock(long accountId)
    {
        var sessionLock = TdLibSessionLock.TryAcquire(accountId, TimeSpan.Zero);
        if (sessionLock is null)
            return false;
        if (_sessionLocks.TryAdd(accountId, sessionLock))
            return true;
        sessionLock.Dispose();
        return true;
    }

    public void ReleaseSessionLock(long accountId)
    {
        if (_sessionLocks.TryRemove(accountId, out var sessionLock))
            sessionLock.Dispose();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            WriteStartupError(args.Exception);
            args.Handled = true;
            MessageBox.Show(
                "เปิดโปรแกรมไม่สำเร็จ ระบบบันทึกรายละเอียดไว้ที่ %LOCALAPPDATA%\\TelegramControlStudio\\logs\\startup-crash.log",
                "Telegram Control Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                WriteStartupError(exception);
            }
        };

        try
        {
            _host = await StudioHost.StartAsync();
        }
        catch (Exception exception)
        {
            WriteStartupError(exception);
            MessageBox.Show(
                "เปิดโปรแกรมไม่สำเร็จ ระบบบันทึกรายละเอียดไว้ที่ %LOCALAPPDATA%\\TelegramControlStudio\\logs\\startup-crash.log",
                "Telegram Control Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        foreach (var sessionLock in _sessionLocks.Values)
            sessionLock.Dispose();
        _sessionLocks.Clear();
        base.OnExit(e);
    }

    private static void WriteStartupError(Exception exception)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TelegramControlStudio",
                "logs");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "startup-crash.log");
            File.AppendAllText(
                logPath,
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}");
        }
        catch
        {
            // Startup diagnostics must never prevent shutdown.
        }
    }
}
