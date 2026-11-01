using System.Windows;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.App;

public partial class InviteLinksWindow : Window
{
    private readonly string _databasePath;
    private long _accountId;

    public InviteLinksWindow(string databasePath, long accountId)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _accountId = accountId;
        AccountSelector.Initialize(databasePath, accountId);
        AccountSelector.AccountChanged += OnAccountChanged;
        Loaded += async (_, _) => { await LoadAsync(); await ReloadChatsAsync(); };
    }

    private async void OnAccountChanged(object? sender, long accountId)
    {
        _accountId = accountId;
        StatusText.Text = string.Empty;
        await LoadAsync();
        await ReloadChatsAsync();
    }

    private async Task LoadAsync() => LinksGrid.ItemsSource = await new InviteLinkRepository(_databasePath).ListAsync(_accountId);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    // Typing a chat id is how an operator ends up with a positive id that TDLib has no chat for, so
    // the window offers the ids it already knows. The list is read again whenever it opens because
    // the Groups window may have synced chats in the meantime.
    private async Task ReloadChatsAsync()
    {
        var selected = (ChatBox.SelectedItem as ChatOption)?.Group;
        var groups = await new TelegramGroupRepository(_databasePath).ListAsync(_accountId);
        var options = groups.Select(group => new ChatOption(group)).ToArray();
        ChatBox.ItemsSource = options;
        ChatBox.SelectedItem = options.FirstOrDefault(option => option.Group.ChatId == (selected?.ChatId ?? 0))
            ?? options.FirstOrDefault();
    }

    private async void Chats_DropDownOpened(object sender, System.EventArgs e) => await ReloadChatsAsync();
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
        if (ChatBox.SelectedItem is not ChatOption chat)
        {
            StatusText.Text = "กรุณาเลือกกลุ่มหรือแชนแนลจากรายการก่อน";
            return;
        }
        var chatId = chat.Group.ChatId;
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
        if (MessageBox.Show($"ยืนยันสร้าง Invite Link แบบ Join Request สำหรับ \"{chat.Group.Title}\" (chat_id={chatId})?", "ยืนยัน", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var link = await new TdLibInviteLinkService(_databasePath).CreateAndPersistAsync(client, _accountId, chatId, expiresAt, usageLimit, true, true, true);
            StatusText.Text = $"สร้างสำเร็จ: {link.InviteUrl}";
            await LoadAsync();
        }
        catch (Exception exception) { StatusText.Text = $"สร้างไม่สำเร็จ: {exception.Message}"; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // Another process can hold the clipboard open mid-write, so a failed copy has to be reported here
    // instead of the operator assuming the link is already copied.
    private void CopyInviteLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: InviteLink link }) return;
        try
        {
            Clipboard.SetText(link.InviteUrl);
            StatusText.Text = $"คัดลอก {link.InviteUrl} แล้ว";
        }
        catch (Exception)
        {
            StatusText.Text = "คัดลอกไม่สำเร็จ กรุณาลองอีกครั้ง";
        }
    }

    private sealed record ChatOption(TelegramGroup Group)
    {
        public override string ToString() => $"{Group.Title} • {Group.ChatId}";
    }
}

