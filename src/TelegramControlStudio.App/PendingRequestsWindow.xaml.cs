using System.Windows;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.App;

public partial class PendingRequestsWindow : Window
{
    private readonly string _databasePath;
    private long _accountId;
    public PendingRequestsWindow(string databasePath, long accountId)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _accountId = accountId;
        AccountSelector.Initialize(databasePath, accountId);
        AccountSelector.AccountChanged += OnAccountChanged;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async void OnAccountChanged(object? sender, long accountId)
    {
        _accountId = accountId;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var pending = await new JoinRequestRepository(_databasePath).ListPendingAsync(_accountId);
        RequestsGrid.ItemsSource = pending;
        StatusText.Text = pending.Count == 0
            ? "ไม่มีคำขอที่รออยู่ — Telegram จะส่งคำขอมาเมื่อบัญชีนี้เป็นแอดมินของห้องนั้นและต่อ TDLib อยู่"
            : $"คำขอที่รอตัดสินใจ {pending.Count} รายการ";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Approve_Click(object sender, RoutedEventArgs e) => await DecideAsync("Approved");
    private async void Reject_Click(object sender, RoutedEventArgs e) => await DecideAsync("Rejected");

    private async Task DecideAsync(string decision)
    {
        if (RequestsGrid.SelectedItem is not JoinRequest request)
        {
            MessageBox.Show("กรุณาเลือกรายการก่อน", "ยังไม่ได้เลือก", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(ReasonBox.Text))
        {
            MessageBox.Show("กรุณาระบุเหตุผล", "ข้อมูลไม่ครบ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var confirm = MessageBox.Show($"ยืนยัน {decision} คำขอนี้หรือไม่?", "ยืนยันการตัดสินใจ", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        var app = (App)Application.Current;
        if (!app.TdLibClients.TryGet(_accountId, out var client) || client is null)
        {
            MessageBox.Show("บัญชีนี้ยังไม่ได้เชื่อมต่อและยืนยันตัวตนกับ TDLib", "ยังไม่พร้อมดำเนินการ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            await new TdLibJoinRequestDecisionService(_databasePath).DecideAsync(client, request, decision, ReasonBox.Text.Trim(), true, true);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"ดำเนินการไม่สำเร็จ\n{exception.Message}", "TDLib ไม่ยืนยันคำสั่ง", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await LoadAsync();
        ReasonBox.Clear();
    }
}

