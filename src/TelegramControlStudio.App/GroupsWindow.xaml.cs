using System.Collections.ObjectModel;
using System.Windows;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App;

public partial class GroupsWindow : Window
{
    public ObservableCollection<TelegramGroup> Groups { get; } = [];
    public GroupsWindow(string databasePath, long accountId)
    {
        InitializeComponent(); DataContext = this; _ = LoadAsync(databasePath, accountId);
    }
    private async Task LoadAsync(string databasePath, long accountId)
    {
        try { foreach (var group in await new TelegramGroupRepository(databasePath).ListAsync(accountId)) Groups.Add(group); }
        catch { }
    }
}

