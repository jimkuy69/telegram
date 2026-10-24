using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App;

public partial class GroupMembersWindow : Window
{
    private readonly string _databasePath; private readonly long _accountId; private readonly long _chatId;
    public ObservableCollection<GroupMember> Members { get; } = [];
    public GroupMembersWindow(string databasePath, long accountId, long chatId, string title)
    { InitializeComponent(); _databasePath = databasePath; _accountId = accountId; _chatId = chatId; TitleText.Text = $"สมาชิก • {title}"; DataContext = this; _ = LoadAsync(); }
    private async Task LoadAsync() { if (string.IsNullOrWhiteSpace(_databasePath)) return; foreach (var member in await new GroupMemberRepository(_databasePath).ListAsync(_accountId, _chatId)) Members.Add(member); }
    private async void Search_Click(object sender, RoutedEventArgs e) { Members.Clear(); foreach (var m in await new GroupMemberRepository(_databasePath).ListAsync(_accountId, _chatId, SearchBox.Text)) Members.Add(m); }
    private void Export_Click(object sender, RoutedEventArgs e)
    { var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "group-members.csv" }; if (dialog.ShowDialog() != true) return; var csv = new StringBuilder("account_id,chat_id,telegram_user_id,display_name,username,status,synced_at\n"); foreach (var m in Members) csv.AppendLine($"{m.AccountId},{m.ChatId},{m.TelegramUserId},\"{m.DisplayName.Replace("\"", "\"\"")}\",\"{m.Username ?? ""}\",{m.Status},{m.SyncedAt:O}"); File.WriteAllText(dialog.FileName, csv.ToString(), Encoding.UTF8); MessageBox.Show("Export CSV สำเร็จ", "เรียบร้อย", MessageBoxButton.OK, MessageBoxImage.Information); }
}

