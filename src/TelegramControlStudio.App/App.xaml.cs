using System.IO;
using System.Windows;
using System.Windows.Threading;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.App;

public partial class App : Application
{
    public TdLibAccountClientRegistry TdLibClients { get; } = new();

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
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TelegramControlStudio");
            var database = new SqliteDatabase(Path.Combine(dataDirectory, "telegram-control-studio.db"));
            await database.InitializeAsync();
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
        TdLibClients.DisposeAllAsync().GetAwaiter().GetResult();
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
