using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.App;

public partial class GroupBroadcastWindow : Window
{
    private readonly string _databasePath;
    private long _accountId;
    private ObservableCollection<GroupBroadcastRow> _rows = [];
    private ICollectionView? _view;
    private SendSafetySettings _safety = SendSafetySettings.SafeDefault;
    private string _search = string.Empty;
    private bool _busy;

    public GroupBroadcastWindow(string databasePath, long accountId)
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
        var groups = await new TelegramGroupRepository(_databasePath).ListAsync(_accountId);
        foreach (var row in _rows) row.PropertyChanged -= Row_PropertyChanged;
        _rows = new ObservableCollection<GroupBroadcastRow>(groups.Select(group => new GroupBroadcastRow(group)));
        foreach (var row in _rows) row.PropertyChanged += Row_PropertyChanged;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = Matches;
        GroupGrid.ItemsSource = _view;
        await RefreshSafetyAsync();
    }

    private async Task RefreshSafetyAsync()
    {
        _safety = await new SendSafetyRepository(_databasePath).GetAsync();
        SafetyText.Text = SendSafetyPolicy.Describe(_safety, _safety.AllowlistUserIds.Count)
            + " • โพสต์เข้ากลุ่ม/แชนแนลของตัวเอง ไม่ต้องขอ Consent • หยุดฉุกเฉินบังคับทุกโพสต์";
        UpdateSelection();
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupBroadcastRow.Selected)) UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selected = _rows.Where(row => row.Selected).ToArray();
        if (selected.Length == 0)
        {
            SelectionText.Text = $"ยังไม่ได้เลือก • ทั้งหมด {_rows.Count} กลุ่ม/แชนแนล";
            return;
        }
        var notPostable = selected.Count(row => !row.Group.CanSendMessages);
        SelectionText.Text = $"เลือก {selected.Length} • จะส่งได้ {selected.Length - notPostable} • ไม่มีสิทธิ์โพสต์ {notPostable}";
    }

    private bool Matches(object item)
    {
        if (item is not GroupBroadcastRow row) return false;
        if (_search.Length == 0) return true;
        return row.Title.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || row.ChatId.ToString().Contains(_search, StringComparison.Ordinal);
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
        foreach (var row in _view.Cast<GroupBroadcastRow>()) row.Selected = true;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.Selected = false;
    }

    private void Message_TextChanged(object sender, TextChangedEventArgs e) =>
        CounterText.Text = $"{MessageBoxInput.Text.Length} / 4096";

    private void CopyChatId_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GroupBroadcastRow row })
            CopyToClipboard(row.ChatId.ToString(), "Chat ID");
    }

    // The clipboard belongs to the whole desktop and another process can hold it open mid-write.
    private static void CopyToClipboard(string text, string label)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
            MessageBox.Show($"คัดลอก{label}แล้ว", "คัดลอกแล้ว", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception)
        {
            MessageBox.Show($"คัดลอก{label}ไม่สำเร็จ กรุณาลองอีกครั้ง", "คัดลอกไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!TryGetClient(out var client)) return;
        SetBusy(true);
        try
        {
            StatusText.Text = "กำลังขอกลุ่มจาก Telegram ...";
            var groups = await new TdLibGroupQueryService(client!, _databasePath).SyncMainChatsAsync(_accountId);
            await LoadAsync();
            StatusText.Text = $"ซิงก์สำเร็จ {groups.Count} กลุ่ม/แชนแนล — ติ๊กเลือกแล้วกดส่งได้เลย";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"ซิงก์ไม่สำเร็จ: {exception.Message}";
            MessageBox.Show($"ซิงก์กลุ่มไม่สำเร็จ\n{exception.Message}", "ซิงก์ไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        var selected = _rows.Where(row => row.Selected).Select(row => row.Group).ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show("ยังไม่ได้ติ๊กกลุ่ม", "ยังไม่พร้อมส่ง", MessageBoxButton.OK, MessageBoxImage.Information);
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

        var postable = selected.Count(group => group.CanSendMessages);
        var excluded = selected.Length - postable;
        if (postable == 0)
        {
            MessageBox.Show("ไม่มีกลุ่มที่โพสต์ได้ (ทุกกลุ่มที่ติ๊กไม่มีสิทธิ์โพสต์ตามการซิงก์ล่าสุด)",
                "ส่งไม่ได้", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirmation = new StringBuilder();
        confirmation.AppendLine("ยืนยันโพสต์เข้ากลุ่มจริง");
        confirmation.AppendLine();
        confirmation.AppendLine($"ติ๊ก {selected.Length} กลุ่ม • จะโพสต์ได้ {postable} กลุ่ม");
        if (excluded > 0)
            confirmation.AppendLine($"ไม่มีสิทธิ์โพสต์ {excluded} กลุ่ม จะถูกข้าม (สิทธิ์ตามการซิงก์ล่าสุด — Telegram อาจยังปฏิเสธเพิ่มถ้าสิทธิ์เปลี่ยน)");
        confirmation.AppendLine($"หยุดฉุกเฉิน: {(_safety.KillSwitchEngaged ? "กำลังเปิด — ส่งไม่ได้" : "ปิด")}");
        if (postable > 50)
            confirmation.AppendLine($"เตือน: มากกว่า 50 กลุ่ม เสี่ยงติด flood-wait หรือถูกจำกัดการส่งจาก Telegram");
        confirmation.AppendLine($"เว้นช่วง {intervalSeconds} วินาที/กลุ่ม • เริ่มแล้วหยุดได้ด้วยปุ่มหยุดฉุกเฉิน");
        confirmation.AppendLine();
        confirmation.Append("ยืนยันหรือไม่?");
        if (MessageBox.Show(confirmation.ToString(), "ยืนยันการโพสต์จริง", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var app = (App)Application.Current;
        SetBusy(true);
        StatusText.Text = "กำลังสร้างงาน ...";
        try
        {
            var result = await new GroupBroadcastQueueService(_databasePath).QueueAsync(
                _accountId, selected, text, LinkBox.Text, true);
            if (!app.JobGate.TryBegin(result.JobId))
            {
                await new JobRepository(_databasePath).UpdateStatusAsync(result.JobId, JobStatus.Cancelled, "GROUP_BROADCAST_ALREADY_RUNNING");
                StatusText.Text = "งานนี้กำลังถูกส่งอยู่";
                return;
            }
            try
            {
                await new JobRepository(_databasePath).UpdateStatusAsync(result.JobId, JobStatus.Queued);
                StatusText.Text = $"เริ่มโพสต์ {result.Groups} กลุ่ม ...";
                var run = new TdLibCampaignJobRunner(_databasePath)
                    .RunAsync(client!, result.JobId, _accountId, true, TimeSpan.FromSeconds(intervalSeconds));
                while (true)
                {
                    var finished = await Task.WhenAny(run, Task.Delay(700));
                    var items = await new JobItemRepository(_databasePath).ListAsync(result.JobId);
                    StatusText.Text = $"กำลังโพสต์ {items.Count(IsSettled)}/{items.Count} กลุ่ม ...";
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

    private async Task ShowSummaryAsync(GroupBroadcastQueueResult result, JobStatus status, int intervalSeconds)
    {
        var items = await new JobItemRepository(_databasePath).ListAsync(result.JobId);
        var completed = items.Count(item => item.Status == "Completed");
        var skipped = items.Where(item => item.Status == "Skipped").ToArray();
        var failed = items.Where(item => item.Status == "Failed").ToArray();
        var summary = new StringBuilder();
        summary.AppendLine($"สถานะงาน: {status}");
        summary.AppendLine($"โพสต์สำเร็จ {completed} • ข้าม {skipped.Length} • ผิดพลาด {failed.Length} • รวม {items.Count} กลุ่ม");
        if (result.NotPostableExcluded > 0)
            summary.AppendLine($"ไม่มีสิทธิ์โพสต์ตามการซิงก์ จึงไม่ถูกใส่งาน {result.NotPostableExcluded} กลุ่ม");
        foreach (var group in skipped.GroupBy(item => DescribeReason(item.LastError)).OrderByDescending(group => group.Count()).Take(5))
            summary.AppendLine($"• ข้าม — {group.Key}: {group.Count()} กลุ่ม");
        foreach (var group in failed.GroupBy(item => Summarize(item.LastError)).OrderByDescending(group => group.Count()).Take(3))
            summary.AppendLine($"• ผิดพลาด — {group.Key}: {group.Count()} กลุ่ม");
        summary.AppendLine();
        summary.AppendLine($"Job: {result.JobId}");
        StatusText.Text = $"จบแล้ว: โพสต์สำเร็จ {completed} • ข้าม {skipped.Length} • ผิดพลาด {failed.Length} • {intervalSeconds} วินาที/กลุ่ม";
        MessageBox.Show(summary.ToString(), "ผลการโพสต์", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static bool IsSettled(JobItem item) => item.Status is "Completed" or "Skipped" or "Failed";

    private static string DescribeReason(string? code) => code switch
    {
        SendSafetyPolicy.KillSwitchReason => "หยุดฉุกเฉิน",
        "CHAT_ID_INVALID" => "Chat ID ไม่ถูกต้อง",
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
        SendButton.Content = busy ? "กำลังทำงาน ..." : "ส่งเข้ากลุ่ม";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class GroupBroadcastRow : INotifyPropertyChanged
{
    private bool _selected;

    public GroupBroadcastRow(TelegramGroup group)
    {
        Group = group;
        Title = string.IsNullOrWhiteSpace(group.Title) ? "(ไม่มีชื่อ)" : group.Title;
    }

    public TelegramGroup Group { get; }
    public string Title { get; }
    public long ChatId => Group.ChatId;
    public string KindLabel => Group.Kind switch
    {
        "Group" => "กลุ่ม",
        "Channel" => "แชนแนล",
        _ => "อื่น ๆ"
    };
    public string MemberLabel => Group.MemberCount?.ToString() ?? "—";
    public string PostRights => Group.CanSendMessages ? "โพสต์ได้" : "ไม่มีสิทธิ์โพสต์";

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

    public override string ToString() => $"{Title} • {KindLabel} • {PostRights}";
}
