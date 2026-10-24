using System.Windows;
using System.IO;
using TelegramControlStudio.App.ViewModels;

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
        _ = LoadContactsAsync(viewModel, databasePath);
        _ = LoadAccountsAsync(viewModel, databasePath);
    }

    private static async Task LoadAccountsAsync(MainWindowViewModel viewModel, string databasePath)
    {
        try
        {
            await viewModel.LoadAccountsAsync(databasePath);
        }
        catch
        {
            // The dashboard remains usable before the first local account exists.
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
