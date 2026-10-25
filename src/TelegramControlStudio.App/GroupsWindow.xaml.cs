using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.App;

public partial class GroupsWindow : Window
{
    private readonly string _databasePath;
    private readonly long _accountId;
    public ObservableCollection<TelegramGroup> Groups { get; } = [];
    public GroupsWindow(string databasePath, long accountId)
    {
        InitializeComponent(); _databasePath = databasePath; _accountId = accountId; DataContext = this; _ = LoadAsync(databasePath, accountId);
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

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        if (!app.TdLibClients.TryGet(_accountId, out var client) || client is null)
        {
            MessageBox.Show("บัญชีนี้ยังไม่ได้เชื่อมต่อและยืนยันตัวตนกับ TDLib", "ยังไม่พร้อม Sync", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var groups = await new TdLibGroupQueryService(client, _databasePath).SyncMainChatsAsync(_accountId);
            Groups.Clear();
            foreach (var group in groups) Groups.Add(group);
            StatusText.Text = $"Sync สำเร็จ {groups.Count} กลุ่ม/แชนแนล • ข้อมูลตามที่ Telegram เปิดเผย";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Sync ไม่สำเร็จ: {exception.Message}";
        }
    }
}

