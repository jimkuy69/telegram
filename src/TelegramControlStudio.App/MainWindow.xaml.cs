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
}
