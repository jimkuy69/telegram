using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Infrastructure.Hosting;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.App;

public partial class App : Application
{
    private static readonly TimeSpan AutoRunTick = TimeSpan.FromSeconds(15);

    private StudioHost? _host;
    private readonly ConcurrentDictionary<long, TdLibSessionLock> _sessionLocks = new();
    private readonly JobInFlightGate _jobGate = new();
    private CancellationTokenSource? _autoRunCancellation;
    private Task? _autoRunLoop;

    public StudioHost Host => _host ?? throw new InvalidOperationException("Studio host has not started.");

    public TdLibAccountClientRegistry TdLibClients => Host.TdLibClients;

    /// <summary>Shared by the queue window, the campaign wizard, and the auto runner.</summary>
    public JobInFlightGate JobGate => _jobGate;

    /// <summary>
    /// Holds the cross-process TDLib session lock for an account so two instances of
    /// this app (or a future companion host) can never open the same TDLib session
    /// database at once.
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
            return;
        }

        _autoRunCancellation = new CancellationTokenSource();
        var autoRunner = new CampaignAutoRunner(Host.DatabasePath, TdLibClients, _jobGate);
        _autoRunLoop = Task.Run(() => autoRunner.RunLoopAsync(AutoRunTick, _autoRunCancellation.Token));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _autoRunCancellation?.Cancel();
        try
        {
            _autoRunLoop?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The loop reports shutdown races through the awaited task; exit must continue.
        }
        _autoRunCancellation?.Dispose();
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
