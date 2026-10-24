using System.Windows;
using System.IO;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.App.ViewModels;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainWindowViewModel();
        DataContext = viewModel;
        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TelegramControlStudio",
            "telegram-control-studio.db");
        _ = LoadAccountsAsync(viewModel, databasePath);
    }

    private static async Task LoadAccountsAsync(MainWindowViewModel viewModel, string databasePath)
    {
        try
        {
            await new SqliteDatabase(databasePath).InitializeAsync();
            await viewModel.LoadAccountsAsync(databasePath);
            await RecoverJobsAsync(databasePath, viewModel.Accounts);
            if (viewModel.ActiveAccount is not null)
                await viewModel.LoadContactsAsync(databasePath, viewModel.ActiveAccount.Id);
        }
        catch
        {
            // The dashboard remains usable before the first local account exists.
        }
    }

    private static async Task RecoverJobsAsync(string databasePath, IEnumerable<Account> accounts)
    {
        var jobs = new JobRepository(databasePath);
        var logs = new ActivityLogRepository(databasePath);
        foreach (var account in accounts)
        {
            await jobs.ResumeEligibleJobsAsync(account.Id, DateTimeOffset.UtcNow);
            var paused = await jobs.PauseRunningJobsAsync(account.Id);
            if (paused > 0)
            {
                await logs.AppendAsync(new ActivityLog(
                    account.Id,
                    null,
                    "Info",
                    "SafeStartupRecovery",
                    $"Paused {paused} running job(s) during startup recovery.",
                    DateTimeOffset.UtcNow));
            }
        }
    }

    private static async Task LoadContactsAsync(MainWindowViewModel viewModel, string databasePath)
    {
        try
        {
            await viewModel.LoadContactsAsync(databasePath, accountId: 0);
        }
        catch
        {
            // Startup must remain usable while no authenticated account exists.
        }
    }

    private void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TelegramControlStudio",
            "telegram-control-studio.db");
        var wizard = new AddAccountWindow(databasePath) { Owner = this };
        if (wizard.ShowDialog() == true)
            Title = "Telegram Control Studio • บันทึกบัญชีแล้ว";
    }
}

