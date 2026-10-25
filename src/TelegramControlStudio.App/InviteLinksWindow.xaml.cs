using System.Windows;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;

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
    private async void Revoke_Click(object sender, RoutedEventArgs e)
    {
        if (LinksGrid.SelectedItem is not TelegramControlStudio.Core.Domain.InviteLink link)
        {
            StatusText.Text = "กรุณาเลือกลิงก์ก่อน";
            return;
        }
        var app = (App)Application.Current;
        if (!app.TdLibClients.TryGet(_accountId, out var client) || client is null)
        {
            StatusText.Text = "บัญชียังไม่เชื่อมต่อและยืนยันตัวตนกับ TDLib";
            return;
        }
        if (MessageBox.Show("ยืนยัน Revoke Invite Link นี้? ลิงก์จะใช้เข้าร่วมไม่ได้อีก", "ยืนยัน Revoke", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await new TdLibInviteLinkService(_databasePath).RevokeAsync(client, _accountId, link.ChatId, link.InviteUrl, true, true);
            StatusText.Text = "Revoke สำเร็จ";
            await LoadAsync();
        }
        catch (Exception exception) { StatusText.Text = $"Revoke ไม่สำเร็จ: {exception.Message}"; }
    }
    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(ChatIdBox.Text.Trim(), out var chatId) || chatId == 0)
        {
            StatusText.Text = "กรุณาระบุ Chat ID ให้ถูกต้อง";
            return;
        }
        int? usageLimit = null;
        if (!string.IsNullOrWhiteSpace(UsageLimitBox.Text))
        {
            if (!int.TryParse(UsageLimitBox.Text.Trim(), out var parsed) || parsed < 1) { StatusText.Text = "Usage limit ไม่ถูกต้อง"; return; }
            usageLimit = parsed;
        }
        DateTimeOffset? expiresAt = null;
        if (!string.IsNullOrWhiteSpace(ExpiryHoursBox.Text))
        {
            if (!double.TryParse(ExpiryHoursBox.Text.Trim(), out var hours) || hours <= 0) { StatusText.Text = "จำนวนชั่วโมงหมดอายุไม่ถูกต้อง"; return; }
            expiresAt = DateTimeOffset.UtcNow.AddHours(hours);
        }
        var app = (App)Application.Current;
        if (!app.TdLibClients.TryGet(_accountId, out var client) || client is null)
        {
            StatusText.Text = "บัญชียังไม่เชื่อมต่อและยืนยันตัวตนกับ TDLib";
            return;
        }
        if (MessageBox.Show("ยืนยันสร้าง Invite Link แบบ Join Request?", "ยืนยัน", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var link = await new TdLibInviteLinkService(_databasePath).CreateAndPersistAsync(client, _accountId, chatId, expiresAt, usageLimit, true, true, true);
            StatusText.Text = $"สร้างสำเร็จ: {link.InviteUrl}";
            await LoadAsync();
        }
        catch (Exception exception) { StatusText.Text = $"สร้างไม่สำเร็จ: {exception.Message}"; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

