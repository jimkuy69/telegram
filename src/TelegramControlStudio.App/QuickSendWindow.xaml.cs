using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.App;

public partial class QuickSendWindow : Window
{
    private readonly string _databasePath;
    private long _accountId;
    private ObservableCollection<QuickSendRow> _rows = [];
    private ICollectionView? _view;
    private SendSafetySettings _safety = SendSafetySettings.SafeDefault;
    private string _search = string.Empty;
    private bool _busy;

    public QuickSendWindow(string databasePath, long accountId)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _accountId = accountId;
        AccountSelector.Initialize(databasePath, accountId);
        AccountSelector.AccountChanged += async (_, id) =>
        {
            _accountId = id;
            await LoadAsync();
        };
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var contacts = await new ContactRepository(_databasePath).ListAsync(_accountId);
        foreach (var row in _rows) row.PropertyChanged -= Row_PropertyChanged;
        _rows = new ObservableCollection<QuickSendRow>(contacts.Select(contact => new QuickSendRow(contact)));
        foreach (var row in _rows) row.PropertyChanged += Row_PropertyChanged;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = Matches;
        RecipientGrid.ItemsSource = _view;
        await RefreshSafetyAsync();
    }

    private async Task RefreshSafetyAsync()
    {
        _safety = await new SendSafetyRepository(_databasePath).GetAsync();
        var note = _safety.TestModeEnabled
            ? " • ID ที่ไม่อยู่ใน allowlist จะถูกข้าม (ไปแก้ที่ ⚙ ตั้งค่า)"
            : string.Empty;
        SafetyText.Text = SendSafetyPolicy.Describe(_safety, _safety.AllowlistUserIds.Count) + note;
        UpdateSelection();
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickSendRow.Selected)) UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selected = _rows.Where(row => row.Selected).ToArray();
        if (selected.Length == 0)
        {
            SelectionText.Text = $"ยังไม่ได้เลือก • ทั้งหมด {_rows.Count} รายชื่อ";
            return;
        }
        var optedOut = selected.Count(row => row.Contact.IsOptedOut);
        var notConsented = selected.Count(row => !row.Contact.HasConsent && !row.Contact.IsOptedOut);
        var eligible = selected.Count(row => IsEligible(row.Contact));
        SelectionText.Text = $"เลือก {selected.Length} • จะส่งได้ {eligible} • ยังไม่เคยยินยอม {notConsented} • opt-out {optedOut}";
    }

    private bool IsEligible(Contact contact) =>
        !contact.IsOptedOut && (!_safety.TestModeEnabled || _safety.AllowlistUserIds.Contains(contact.TelegramUserId));

    private bool Matches(object item)
    {
        if (item is not QuickSendRow row) return false;
        if (_search.Length == 0) return true;
        return row.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || row.Username.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || row.Phone.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || row.Id.ToString().Contains(_search, StringComparison.Ordinal);
    }

    private void ApplySearch()
    {
        _search = SearchBox.Text.Trim();
        _view?.Refresh();
        UpdateSelection();
    }

    private void Search_Click(object sender, RoutedEventArgs e) => ApplySearch();

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        ApplySearch();
    }

    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplySearch();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_view is null) return;
        foreach (var row in _view.Cast<QuickSendRow>()) row.Selected = true;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.Selected = false;
    }

    private void Message_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        CounterText.Text = $"{MessageBoxInput.Text.Length} / 4096";

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!TryGetClient(out var client)) return;
        SetBusy(true);
        try
        {
            StatusText.Text = "กำลังขอรายชื่อจาก Telegram ...";
            var progress = new Progress<(int Done, int Total)>(value =>
                StatusText.Text = $"กำลังซิงก์รายชื่อ {value.Done}/{value.Total} ...");
            var contacts = await new TdLibContactSyncService(client!, _databasePath).SyncAsync(_accountId, progress);
            await LoadAsync();
            StatusText.Text = $"ซิงก์สำเร็จ {contacts.Count} รายชื่อ — ติ๊กเลือกแล้วกดส่งได้เลย";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"ซิงก์ไม่สำเร็จ: {exception.Message}";
            MessageBox.Show($"ซิงก์รายชื่อไม่สำเร็จ\n{exception.Message}", "ซิงก์ไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!TryGetClient(out var client)) return;

        var selected = _rows.Where(row => row.Selected).Select(row => row.Contact).ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show("ยังไม่ได้ติ๊กผู้รับ", "ยังไม่พร้อมส่ง", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var text = MessageBoxInput.Text.Trim();
        if (text.Length == 0)
        {
            MessageBox.Show("กรุณาพิมพ์ข้อความก่อนกดส่ง", "ยังไม่มีข้อความ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (text.Length > 4096)
        {
            MessageBox.Show("ข้อความยาวเกิน 4096 ตัวอักษร", "ข้อความยาวเกิน", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(IntervalBox.Text.Trim(), out var intervalSeconds) || intervalSeconds is < 1 or > 120)
        {
            MessageBox.Show("ช่วงห่างต้องเป็นตัวเลข 1-120 วินาที", "ช่วงห่างไม่ถูกต้อง", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await RefreshSafetyAsync();
        if (_safety.KillSwitchEngaged)
        {
            MessageBox.Show("หยุดฉุกเฉินอยู่ ต้องปลดก่อนจึงจะส่งได้", "หยุดฉุกเฉิน", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var optedOut = selected.Count(contact => contact.IsOptedOut);
        var notConsented = selected.Count(contact => !contact.HasConsent && !contact.IsOptedOut);
        var eligible = selected.Count(IsEligible);
        if (eligible == 0)
        {
            MessageBox.Show("ไม่มีผู้รับที่ผ่านเงื่อนไขส่ง (opt-out หรือไม่อยู่ใน allowlist ของ TEST MODE)",
                "ส่งไม่ได้", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirmation = new StringBuilder();
        confirmation.AppendLine("ยืนยันส่งข้อความจริง");
        confirmation.AppendLine();
        confirmation.AppendLine($"ผู้รับที่ติ๊ก {selected.Length} คน • จะส่งได้ {eligible} คน");
        if (notConsented > 0)
            confirmation.AppendLine($"ยังไม่เคยยินยอม {notConsented} คน — การกด \"ใช่\" ถือว่ายืนยันว่าคนเหล่านี้ตกลงรับข้อความ และระบบจะบันทึกว่ายินยอม");
        if (optedOut > 0) confirmation.AppendLine($"opt-out จะถูกข้าม {optedOut} คน");
        if (_safety.TestModeEnabled)
            confirmation.AppendLine($"TEST MODE เปิด: ส่งได้เฉพาะ {_safety.AllowlistUserIds.Count} ID ใน allowlist");
        if (eligible > 200)
            confirmation.AppendLine($"เตือน: มากกว่า 200 คน เสี่ยงติด flood-wait หรือถูกจำกัดการส่งจาก Telegram");
        confirmation.AppendLine($"เว้นช่วง {intervalSeconds} วินาที/คน • เริ่มแล้วหยุดได้ด้วยปุ่มหยุดฉุกเฉิน");
        confirmation.AppendLine();
        confirmation.Append("ยืนยันหรือไม่?");
        if (MessageBox.Show(confirmation.ToString(), "ยืนยันการส่งจริง", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var app = (App)Application.Current;
        SetBusy(true);
        StatusText.Text = "กำลังสร้างงาน ...";
        try
        {
            var result = await new QuickSendQueueService(_databasePath).QueueAsync(
                _accountId, text, LinkBox.Text, selected, true);
            if (!app.JobGate.TryBegin(result.JobId))
            {
                await new JobRepository(_databasePath).UpdateStatusAsync(result.JobId, JobStatus.Cancelled, "QUICK_SEND_ALREADY_RUNNING");
                StatusText.Text = "งานนี้กำลังถูกส่งอยู่";
                return;
            }
            try
            {
                await new JobRepository(_databasePath).UpdateStatusAsync(result.JobId, JobStatus.Queued);
                StatusText.Text = $"เริ่มส่ง {result.Recipients} คน ...";
                var run = new TdLibCampaignJobRunner(_databasePath)
                    .RunAsync(client!, result.JobId, _accountId, true, TimeSpan.FromSeconds(intervalSeconds));
                while (true)
                {
                    var finished = await Task.WhenAny(run, Task.Delay(700));
                    var items = await new JobItemRepository(_databasePath).ListAsync(result.JobId);
                    StatusText.Text = $"กำลังส่ง {items.Count(IsSettled)}/{items.Count} คน ...";
                    if (finished == run) break;
                }
                var status = await run;
                await ShowSummaryAsync(result, status, intervalSeconds);
            }
            finally
            {
                app.JobGate.End(result.JobId);
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"ส่งไม่สำเร็จ: {exception.Message}";
            MessageBox.Show($"ส่งไม่สำเร็จ\n{exception.Message}", "ส่งไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ShowSummaryAsync(QuickSendQueueResult result, JobStatus status, int intervalSeconds)
    {
        var items = await new JobItemRepository(_databasePath).ListAsync(result.JobId);
        var completed = items.Count(item => item.Status == "Completed");
        var skipped = items.Where(item => item.Status == "Skipped").ToArray();
        var failed = items.Where(item => item.Status == "Failed").ToArray();
        var summary = new StringBuilder();
        summary.AppendLine($"สถานะงาน: {status}");
        summary.AppendLine($"ส่งสำเร็จ {completed} • ข้าม {skipped.Length} • ผิดพลาด {failed.Length} • รวม {items.Count}");
        if (result.ConsentAsserted > 0)
            summary.AppendLine($"บันทึกความยินยอมจากการยืนยันครั้งนี้ {result.ConsentAsserted} คน");
        foreach (var group in skipped.GroupBy(item => DescribeReason(item.LastError)).OrderByDescending(group => group.Count()).Take(5))
            summary.AppendLine($"• ข้าม — {group.Key}: {group.Count()} คน");
        foreach (var group in failed.GroupBy(item => Summarize(item.LastError)).OrderByDescending(group => group.Count()).Take(3))
            summary.AppendLine($"• ผิดพลาด — {group.Key}: {group.Count()} คน");
        summary.AppendLine();
        summary.AppendLine($"Job: {result.JobId}");
        StatusText.Text = $"จบแล้ว: ส่งสำเร็จ {completed} • ข้าม {skipped.Length} • ผิดพลาด {failed.Length} • {intervalSeconds} วินาที/คน";
        MessageBox.Show(summary.ToString(), "ผลการส่ง", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static bool IsSettled(JobItem item) => item.Status is "Completed" or "Skipped" or "Failed";

    private static string DescribeReason(string? code) => code switch
    {
        SendSafetyPolicy.ConsentReason => "ไม่ได้ยินยอมหรือ opt-out",
        SendSafetyPolicy.TestModeReason => "ไม่อยู่ใน allowlist ของ TEST MODE",
        SendSafetyPolicy.KillSwitchReason => "หยุดฉุกเฉิน",
        "CONTACT_NOT_FOUND" => "ไม่พบผู้ติดต่อในฐานข้อมูล",
        null or "" => "ไม่ทราบสาเหตุ",
        _ => code
    };

    private static string Summarize(string? message) =>
        string.IsNullOrWhiteSpace(message) ? "ไม่ทราบสาเหตุ"
        : message.Length <= 80 ? message : message[..80] + "…";

    private bool TryGetClient(out ITdLibClient? client)
    {
        var app = (App)Application.Current;
        if (!app.TdLibClients.TryGet(_accountId, out client) || client is null)
        {
            MessageBox.Show("บัญชีนี้ยังไม่ได้เชื่อมต่อ Telegram (กด \"เข้าสู่ระบบ\" ที่หน้าหลักก่อน)",
                "ยังไม่พร้อมส่ง", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return true;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SendButton.IsEnabled = !busy;
        SendButton.Content = busy ? "กำลังทำงาน ..." : "ส่งเลย";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class QuickSendRow : INotifyPropertyChanged
{
    private bool _selected;

    public QuickSendRow(Contact contact)
    {
        Contact = contact;
        Name = BuildName(contact);
        Username = string.IsNullOrWhiteSpace(contact.Username) ? "—" : "@" + contact.Username;
        Phone = string.IsNullOrWhiteSpace(contact.PhoneNumber) ? "—" : contact.PhoneNumber;
        Consent = ContactConsentPolicy.Label(ContactConsentPolicy.Classify(contact.HasConsent, contact.IsOptedOut));
    }

    public Contact Contact { get; }
    public string Name { get; }
    public string Username { get; }
    public string Phone { get; }
    public long Id => Contact.TelegramUserId;
    public string Consent { get; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => $"{Name} • {Consent}";

    private static string BuildName(Contact contact)
    {
        var name = string.Join(' ', new[] { contact.FirstName, contact.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return name.Length == 0 ? "(ไม่มีชื่อ)" : name;
    }
}
