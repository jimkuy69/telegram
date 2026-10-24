using System.Windows;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App;

public partial class InviteLinksWindow : Window
{
    private readonly string _databasePath;
    private readonly long _accountId;

    public InviteLinksWindow(string databasePath, long accountId)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _accountId = accountId;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync() => LinksGrid.ItemsSource = await new InviteLinkRepository(_databasePath).ListAsync(_accountId);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}