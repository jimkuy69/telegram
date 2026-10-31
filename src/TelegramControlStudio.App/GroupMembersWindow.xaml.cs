using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.App;

public partial class GroupMembersWindow : Window
{
    private readonly string _databasePath; private readonly long _accountId; private readonly long _chatId; private readonly bool _canViewMembers;
    private const int PageSize = 100;
    private int _page;
    public ObservableCollection<GroupMember> Members { get; } = [];
    public GroupMembersWindow(string databasePath, long accountId, long chatId, string title, bool canViewMembers)
    { InitializeComponent(); _databasePath = databasePath; _accountId = accountId; _chatId = chatId; _canViewMembers = canViewMembers; TitleText.Text = $"สมาชิก • {title}"; DataContext = this; _ = LoadPageAsync(); }
    private async Task LoadPageAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        try
        {
            var members = await new GroupMemberRepository(_databasePath).ListAsync(_accountId, _chatId, SearchBox.Text, _page * PageSize, PageSize);
            Members.Clear(); foreach (var member in members) Members.Add(member);
            PageText.Text = $"หน้า {_page + 1} • {members.Count} รายการ";
            PreviousButton.IsEnabled = _page > 0;
            NextButton.IsEnabled = members.Count == PageSize;
            ShowEmptyHint(members.Count);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"โหลดรายชื่อไม่สำเร็จ: {exception.Message}";
        }
    }

    // An empty grid reads as a bug, so say why it is empty: Telegram hides channel members from
    // accounts that are not administrators, and no amount of syncing brings them back.
    private void ShowEmptyHint(int count)
    {
        if (count > 0) { EmptyPanel.Visibility = Visibility.Collapsed; return; }
        EmptyText.Text = !string.IsNullOrWhiteSpace(SearchBox.Text)
            ? "ไม่พบรายชื่อตามคำค้นหา"
            : _canViewMembers
                ? "ยังไม่มีข้อมูลสมาชิก — กด Sync TDLib เพื่อดึงรายชื่อเท่าที่ Telegram เปิดเผย"
                : "แชนแนล/กลุ่มนี้ไม่เปิดเผยรายชื่อสมาชิกให้บัญชีนี้ (Telegram จำกัดสิทธิ์) — กด Sync TDLib ก็ไม่ทำให้รายชื่อขึ้น";
        EmptyPanel.Visibility = Visibility.Visible;
    }
    private async void Search_Click(object sender, RoutedEventArgs e) { _page = 0; await LoadPageAsync(); }
    private async void Previous_Click(object sender, RoutedEventArgs e) { if (_page == 0) return; _page--; await LoadPageAsync(); }
    private async void Next_Click(object sender, RoutedEventArgs e) { _page++; await LoadPageAsync(); }
    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        if (!app.TdLibClients.TryGet(_accountId, out var client) || client is null) { StatusText.Text = "ยังไม่ได้เชื่อมต่อ TDLib"; return; }
        try { var count = await new TdLibGroupMemberSyncService(client, _databasePath).SyncAsync(_accountId, _chatId, _page * PageSize, PageSize); await LoadPageAsync(); StatusText.Text = $"Sync สมาชิกที่ Telegram เปิดเผย {count} รายการในหน้านี้"; }
        catch (Exception exception) { StatusText.Text = $"Sync ไม่สำเร็จ: {exception.Message}"; }
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    { var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "group-members.csv" }; if (dialog.ShowDialog() != true) return; var csv = new StringBuilder("account_id,chat_id,telegram_user_id,display_name,username,status,synced_at\n"); foreach (var m in Members) csv.AppendLine($"{m.AccountId},{m.ChatId},{m.TelegramUserId},\"{m.DisplayName.Replace("\"", "\"\"")}\",\"{m.Username ?? ""}\",{m.Status},{m.SyncedAt:O}"); File.WriteAllText(dialog.FileName, csv.ToString(), Encoding.UTF8); MessageBox.Show("Export CSV สำเร็จ", "เรียบร้อย", MessageBoxButton.OK, MessageBoxImage.Information); }
}
