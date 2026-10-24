using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App;

public partial class GroupsWindow : Window
{
    private readonly string _databasePath;
    public ObservableCollection<TelegramGroup> Groups { get; } = [];
    public GroupsWindow(string databasePath, long accountId)
    {
        InitializeComponent(); _databasePath = databasePath; DataContext = this; _ = LoadAsync(databasePath, accountId);
    }
    private async Task LoadAsync(string databasePath, long accountId)
    {
        try { foreach (var group in await new TelegramGroupRepository(databasePath).ListAsync(accountId)) Groups.Add(group); }
        catch { }
    }

    private void Group_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as System.Windows.Controls.DataGrid)?.SelectedItem is not TelegramGroup group) return;
        new GroupMembersWindow(_databasePath, group.AccountId, group.ChatId, group.Title) { Owner = this }.ShowDialog();
    }
}

