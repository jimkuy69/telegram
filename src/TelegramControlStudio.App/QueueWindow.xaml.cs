using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Safety;
using TelegramControlStudio.Infrastructure.Telegram;
namespace TelegramControlStudio.App;
public partial class QueueWindow : Window
{
    private static readonly SolidColorBrush TestBannerBg = new(Color.FromRgb(0x2E, 0x23, 0x10));
    private static readonly SolidColorBrush TestBannerBorder = new(Color.FromRgb(0x6B, 0x4B, 0x12));
    private static readonly SolidColorBrush TestBannerText = new(Color.FromRgb(0xFB, 0xBF, 0x24));
    private static readonly SolidColorBrush LiveBannerBg = new(Color.FromRgb(0x10, 0x28, 0x1A));
    private static readonly SolidColorBrush LiveBannerBorder = new(Color.FromRgb(0x1F, 0x5A, 0x38));
    private static readonly SolidColorBrush LiveBannerText = new(Color.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly SolidColorBrush KillBannerBg = new(Color.FromRgb(0x33, 0x14, 0x1B));
    private static readonly SolidColorBrush KillBannerBorder = new(Color.FromRgb(0x5B, 0x23, 0x33));
    private static readonly SolidColorBrush KillBannerText = new(Color.FromRgb(0xF8, 0x71, 0x71));
    private readonly string _databasePath; private readonly long _accountId;
    private CancellationTokenSource? _runCancellation;
    private List<Job> _allJobs = [];
    public ObservableCollection<Job> Jobs { get; } = []; public Job? SelectedJob { get; set; }
    public QueueWindow(string databasePath, long accountId) { InitializeComponent(); _databasePath = databasePath; _accountId = accountId; DataContext = this; Loaded += async (_, _) => await LoadAsync(); }
    private async Task LoadAsync()
    {
        var repository = new JobRepository(_databasePath);
        var jobs = await repository.ListAsync(_accountId);
        var synchronizer = new JobAggregateSynchronizer(_databasePath);
        foreach (var job in jobs) await synchronizer.SynchronizeAsync(job.Id);
        _allJobs = (await repository.ListAsync(_accountId)).ToList();
        ApplyFilter();
        await RefreshSafetyAsync();
    }
    private async Task RefreshSafetyAsync()
    {
        try { ApplySafetyBanner(await new SendSafetyRepository(_databasePath).GetAsync()); }
        catch (Exception) { SafetyBannerText.Text = "อ่านสถานะความปลอดภัยไม่สำเร็จ"; }
    }
    private void ApplySafetyBanner(SendSafetySettings settings)
    {
        SafetyBanner.Background = settings.KillSwitchEngaged ? KillBannerBg : settings.TestModeEnabled ? TestBannerBg : LiveBannerBg;
        SafetyBanner.BorderBrush = settings.KillSwitchEngaged ? KillBannerBorder : settings.TestModeEnabled ? TestBannerBorder : LiveBannerBorder;
        SafetyBannerText.Foreground = settings.KillSwitchEngaged ? KillBannerText : settings.TestModeEnabled ? TestBannerText : LiveBannerText;
        SafetyBannerText.Text = settings.KillSwitchEngaged
            ? "หยุดฉุกเฉินอยู่ — ระบบบล็อกการส่งทั้งหมด กด “ปลดหยุดฉุกเฉิน” เพื่อกลับมาใช้งานตามโหมดที่ตั้งไว้"
            : settings.TestModeEnabled
                ? $"โหมดทดสอบ — ส่งได้เฉพาะ {settings.AllowlistUserIds.Count} รายใน allowlist (เพิ่ม User ID ได้ที่หน้าตั้งค่า)"
                : "โหมดใช้งานจริง — ส่งได้ทุกคนที่ให้ Consent";
        EmergencyStopButton.IsEnabled = !settings.KillSwitchEngaged;
        ClearEmergencyButton.IsEnabled = settings.KillSwitchEngaged;
    }
    private async void EmergencyStop_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("ยืนยันหยุดฉุกเฉิน? ระบบจะหยุดส่งทันที และบล็อกการส่งใหม่ทั้งหมดจนกว่าจะปลดล็อก", "ยืนยันหยุดฉุกเฉิน", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await new SendSafetyRepository(_databasePath).SetKillSwitchAsync(true);
        EmergencyStopSignal.Trigger();
        _runCancellation?.Cancel();
        if (SelectedJob is { Status: JobStatus.Running } running)
        {
            await new JobItemRepository(_databasePath).UpdateStatusForJobAsync(running.Id, "Running", "Queued");
            await new JobRepository(_databasePath).UpdateStatusAsync(running.Id, JobStatus.Paused, SendSafetyPolicy.KillSwitchReason);
        }
        await LogAsync("EmergencyStopEngaged", "Kill switch engaged from Queue UI.");
        await LoadAsync();
        MessageBox.Show("หยุดฉุกเฉินแล้ว งานที่กำลังทำงานถูกหยุดชั่วคราว รายการที่ยังไม่ส่งยังอยู่ในคิว", "หยุดฉุกเฉิน", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private async void ClearEmergency_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("ปลดหยุดฉุกเฉิน? การส่งจะกลับมาทำได้ตามโหมดที่ตั้งไว้", "ปลดหยุดฉุกเฉิน", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await new SendSafetyRepository(_databasePath).SetKillSwitchAsync(false);
        await LogAsync("KillSwitchCleared", "Kill switch cleared from Queue UI.");
        await LoadAsync();
    }
    private Task LogAsync(string eventType, string message) =>
        new ActivityLogRepository(_databasePath).AppendAsync(new ActivityLog(_accountId, SelectedJob?.Id, "Warning", eventType, message, DateTimeOffset.UtcNow));
    private void ApplyFilter()
    {
        var filter = StatusFilterBox?.SelectedItem is ComboBoxItem item ? item.Content?.ToString() : "ทั้งหมด";
        var visible = string.IsNullOrWhiteSpace(filter) || filter == "ทั้งหมด" ? _allJobs : _allJobs.Where(job => job.Status.ToString() == filter).ToList();
        Jobs.Clear();
        foreach (var job in visible) Jobs.Add(job);
        if (QueueSummaryText is not null) QueueSummaryText.Text = $"แสดง {Jobs.Count} จากทั้งหมด {_allJobs.Count} งาน";
    }
    private async Task ChangeAsync(JobStatus status)
    {
        if (SelectedJob is null) return;
        var selectedId = SelectedJob.Id;
        if (await new JobRepository(_databasePath).UpdateStatusAsync(selectedId, status))
            await new ActivityLogRepository(_databasePath).AppendAsync(new ActivityLog(_accountId, selectedId, "Info", $"Job{status}", $"Job {selectedId} changed to {status} from Queue UI.", DateTimeOffset.UtcNow));
        await LoadAsync();
        SelectedJob = Jobs.FirstOrDefault(job => job.Id == selectedId);
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private void StatusFilter_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }
    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJob is null) { MessageBox.Show("กรุณาเลือก Job ก่อน", "ยังไม่ได้เลือก", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (SelectedJob.Status != JobStatus.WaitingForConfirmation) { MessageBox.Show("Job นี้ไม่ได้อยู่ในสถานะรอยืนยัน", "ยืนยันไม่ได้", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if ((await new SendSafetyRepository(_databasePath).GetAsync()).KillSwitchEngaged) { MessageBox.Show("หยุดฉุกเฉินอยู่ ต้องปลดหยุดฉุกเฉินก่อนจึงจะเริ่มส่งได้", "หยุดฉุกเฉิน", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (MessageBox.Show("ยืนยันเริ่มส่งเฉพาะผู้รับที่ผ่าน Consent หรือไม่? การส่งจะเริ่มผ่าน Telegram จริง", "ยืนยันการส่ง", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var movedItems = await new JobItemRepository(_databasePath).UpdateStatusForJobAsync(SelectedJob.Id, JobStatus.WaitingForConfirmation.ToString(), JobStatus.Queued.ToString());
        if (movedItems == 0) { MessageBox.Show("Job นี้ยังไม่มีรายการผู้รับที่รอยืนยัน จึงยังเข้าคิวไม่ได้", "ยังไม่มีผู้รับ", MessageBoxButton.OK, MessageBoxImage.Information); await LoadAsync(); return; }
        await ChangeAsync(JobStatus.Queued); await StartSelectedJobAsync();
    }
    private async Task StartSelectedJobAsync()
    {
        if (SelectedJob is null) return;
        var app = (App)Application.Current;
        if (!app.TdLibClients.TryGet(_accountId, out var client) || client is null) { MessageBox.Show("บัญชียังไม่ได้เชื่อมต่อกับ Telegram หรือ Session หมดอายุ", "ยังไม่พร้อมส่ง", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        _runCancellation?.Dispose(); _runCancellation = new CancellationTokenSource();
        try { await new TdLibCampaignJobRunner(_databasePath).RunAsync(client, SelectedJob.Id, _accountId, true, TimeSpan.FromSeconds(2), _runCancellation.Token); }
        catch (OperationCanceledException)
        {
            // Cancel and Pause share the same cancellation token. Preserve an explicit
            // Cancelled state instead of racing it back to Paused.
            var current = await new JobRepository(_databasePath).GetAsync(SelectedJob.Id);
            if (current?.Status != JobStatus.Cancelled)
                await new JobRepository(_databasePath).UpdateStatusAsync(SelectedJob.Id, JobStatus.Paused, "Paused by user.");
        }
        catch (Exception) { await new JobRepository(_databasePath).UpdateStatusAsync(SelectedJob.Id, JobStatus.Failed, "QUEUE_RUN_FAILED"); await new ActivityLogRepository(_databasePath).AppendAsync(new ActivityLog(_accountId, SelectedJob.Id, "Error", "QueueRunFailed", "Queue runner failed; sensitive data was not logged.", DateTimeOffset.UtcNow)); }
        finally { _runCancellation.Dispose(); _runCancellation = null; await LoadAsync(); }
    }
    private async void Pause_Click(object sender, RoutedEventArgs e) { if (SelectedJob is null || SelectedJob.Status != JobStatus.Running) { MessageBox.Show("เลือก Job ที่กำลังทำงานอยู่ก่อนกดหยุดชั่วคราว", "หยุดชั่วคราวไม่ได้", MessageBoxButton.OK, MessageBoxImage.Information); return; } _runCancellation?.Cancel(); await ChangeAsync(JobStatus.Paused); }
    private async void Resume_Click(object sender, RoutedEventArgs e) { if (SelectedJob is null) { MessageBox.Show("กรุณาเลือก Job ก่อน", "ยังไม่ได้เลือก", MessageBoxButton.OK, MessageBoxImage.Information); return; } if (SelectedJob.Status is not (JobStatus.Paused or JobStatus.WaitingForFloodWait)) { MessageBox.Show("ทำต่อได้เฉพาะงานที่หยุดชั่วคราวหรือรอ FloodWait", "ทำต่อไม่ได้", MessageBoxButton.OK, MessageBoxImage.Information); return; } if ((await new SendSafetyRepository(_databasePath).GetAsync()).KillSwitchEngaged) { MessageBox.Show("หยุดฉุกเฉินอยู่ ต้องปลดหยุดฉุกเฉินก่อนจึงจะทำต่อได้", "หยุดฉุกเฉิน", MessageBoxButton.OK, MessageBoxImage.Warning); return; } await ChangeAsync(JobStatus.Queued); await StartSelectedJobAsync(); }
    private async void Retry_Click(object sender, RoutedEventArgs e) { if (SelectedJob is not null && await new JobRepository(_databasePath).IncrementRetryAsync(SelectedJob.Id, "Manually retried from queue")) { await new ActivityLogRepository(_databasePath).AppendAsync(new ActivityLog(_accountId, SelectedJob.Id, "Info", "JobRetry", $"Job {SelectedJob.Id} manually retried from Queue UI.", DateTimeOffset.UtcNow)); await LoadAsync(); await StartSelectedJobAsync(); } }
    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJob is null) return;
        _runCancellation?.Cancel();
        await new JobItemRepository(_databasePath).UpdateStatusForJobAsync(SelectedJob.Id, JobStatus.Queued.ToString(), JobStatus.Cancelled.ToString());
        await ChangeAsync(JobStatus.Cancelled);
    }
    private void Job_DoubleClick(object sender, MouseButtonEventArgs e) { if (SelectedJob is not null) new JobItemsWindow(_databasePath, SelectedJob.Id) { Owner = this }.ShowDialog(); }
}
