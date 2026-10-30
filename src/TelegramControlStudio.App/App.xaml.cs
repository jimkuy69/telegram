using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Infrastructure.Hosting;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.App;

public partial class App : Application
{
    private static readonly TimeSpan AutoRunTick = TimeSpan.FromSeconds(15);

    private StudioHost? _host;
    private readonly ConcurrentDictionary<long, TdLibSessionLock> _sessionLocks = new();
    private readonly JobInFlightGate _jobGate = new();
    private ConsentReplyDispatcher? _consentReplies;
    private CancellationTokenSource? _autoRunCancellation;
    private Task? _autoRunLoop;

    public StudioHost Host => _host ?? throw new InvalidOperationException("Studio host has not started.");

    public TdLibAccountClientRegistry TdLibClients => Host.TdLibClients;

    /// <summary>Shared by the queue window, the campaign wizard, and the auto runner.</summary>
    public JobInFlightGate JobGate => _jobGate;

    /// <summary>
    /// Gives every session the callback that turns inbound Telegram messages into consent answers.
    /// It is one dispatcher for the whole app, so reconnecting an account cannot stack pumps.
    /// </summary>
    public Func<long, Action<string>?> ConsentReplySinkFactory =>
        accountId => (_consentReplies ??= new ConsentReplyDispatcher(Host.DatabasePath)).SinkFor(accountId);

    public async Task StopConsentReplyWatchAsync(long accountId)
    {
        if (_consentReplies is not null) await _consentReplies.StopAsync(accountId);
    }

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

    /// <summary>
    /// Closes the TDLib client for one account and releases its session lock so another
    /// instance can sign in. Refused while a job for that account is mid-transmission,
    /// because disposing the client underneath the runner would fail the whole job.
    /// </summary>
    public async Task<bool> TryDisconnectAccountAsync(long accountId)
    {
        if (_jobGate.IsAccountBusy(accountId)) return false;
        var jobs = new JobRepository(Host.DatabasePath);
        foreach (var jobId in _jobGate.JobIds)
        {
            var job = await jobs.GetAsync(jobId);
            if (job is not null && job.AccountId == accountId) return false;
        }

        // Reserving the account here is what keeps the auto runner from claiming a job in the
        // window between the checks above and the client being closed.
        if (!_jobGate.TryBeginAccount(accountId)) return false;
        try
        {
            var removed = await Host.TdLibClients.RemoveAsync(accountId);
            await StopConsentReplyWatchAsync(accountId);
            ReleaseSessionLock(accountId);
            return removed;
        }
        finally
        {
            _jobGate.EndAccount(accountId);
        }
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
        _consentReplies?.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
