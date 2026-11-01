using System.Collections.ObjectModel;
using System.Windows;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
namespace TelegramControlStudio.App;
public partial class ActivityLogWindow : Window
{
    private readonly string _databasePath; private long _accountId;
    public ObservableCollection<ActivityLog> Entries { get; } = [];
    public ActivityLogWindow(string databasePath, long accountId) { InitializeComponent(); _databasePath = databasePath; _accountId = accountId; DataContext = this; AccountSelector.Initialize(databasePath, accountId); AccountSelector.AccountChanged += OnAccountChanged; Loaded += async (_, _) => await LoadAsync(); }
    private async void OnAccountChanged(object? sender, long accountId) { _accountId = accountId; await LoadAsync(); }
    private async Task LoadAsync() { Entries.Clear(); foreach (var entry in await new ActivityLogRepository(_databasePath).ListAsync(_accountId, limit: 500)) Entries.Add(entry); }
}

