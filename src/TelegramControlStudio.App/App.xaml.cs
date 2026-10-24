using System.IO;
using System.Windows;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.App;

public partial class App : Application
{
    public TdLibAccountClientRegistry TdLibClients { get; } = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio");
        var database = new SqliteDatabase(Path.Combine(dataDirectory, "telegram-control-studio.db"));
        await database.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TdLibClients.DisposeAllAsync().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}