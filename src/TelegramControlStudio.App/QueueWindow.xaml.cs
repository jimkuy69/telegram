using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
namespace TelegramControlStudio.App;
public partial class QueueWindow : Window
{
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
    }
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
        if (await new JobRepository(_databasePath).UpdateStatusAsync(SelectedJob.Id, status))
            await new ActivityLogRepository(_databasePath).AppendAsync(new ActivityLog(_accountId, SelectedJob.Id, "Info", $"Job{status}", $"Job {SelectedJob.Id} changed to {status} from Queue UI.", DateTimeOffset.UtcNow));
        await LoadAsync();
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private void StatusFilter_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }
    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJob is null) { MessageBox.Show("กรุณาเลือก Job ก่อน", "ยังไม่ได้เลือก", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (SelectedJob.Status != JobStatus.WaitingForConfirmation) { MessageBox.Show("Job นี้ไม่ได้อยู่ในสถานะรอยืนยัน", "ยืนยันไม่ได้", MessageBoxButton.OK, MessageBoxImage.Information); return; }
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
        catch (OperationCanceledException) { await new JobRepository(_databasePath).UpdateStatusAsync(SelectedJob.Id, JobStatus.Paused, "Paused by user."); }
        catch (Exception) { await new JobRepository(_databasePath).UpdateStatusAsync(SelectedJob.Id, JobStatus.Failed, "QUEUE_RUN_FAILED"); await new ActivityLogRepository(_databasePath).AppendAsync(new ActivityLog(_accountId, SelectedJob.Id, "Error", "QueueRunFailed", "Queue runner failed; sensitive data was not logged.", DateTimeOffset.UtcNow)); }
        finally { _runCancellation.Dispose(); _runCancellation = null; await LoadAsync(); }
    }
    private async void Pause_Click(object sender, RoutedEventArgs e) { if (SelectedJob is null || SelectedJob.Status != JobStatus.Running) { MessageBox.Show("เลือก Job ที่กำลังทำงานอยู่ก่อนกดหยุดชั่วคราว", "หยุดชั่วคราวไม่ได้", MessageBoxButton.OK, MessageBoxImage.Information); return; } _runCancellation?.Cancel(); await ChangeAsync(JobStatus.Paused); }
    private async void Resume_Click(object sender, RoutedEventArgs e) { if (SelectedJob is null) { MessageBox.Show("กรุณาเลือก Job ก่อน", "ยังไม่ได้เลือก", MessageBoxButton.OK, MessageBoxImage.Information); return; } if (SelectedJob.Status is not (JobStatus.Paused or JobStatus.WaitingForFloodWait)) { MessageBox.Show("ทำต่อได้เฉพาะงานที่หยุดชั่วคราวหรือรอ FloodWait", "ทำต่อไม่ได้", MessageBoxButton.OK, MessageBoxImage.Information); return; } await ChangeAsync(JobStatus.Queued); await StartSelectedJobAsync(); }
    private async void Retry_Click(object sender, RoutedEventArgs e) { if (SelectedJob is not null && await new JobRepository(_databasePath).IncrementRetryAsync(SelectedJob.Id, "Manually retried from queue")) { await new ActivityLogRepository(_databasePath).AppendAsync(new ActivityLog(_accountId, SelectedJob.Id, "Info", "JobRetry", $"Job {SelectedJob.Id} manually retried from Queue UI.", DateTimeOffset.UtcNow)); await LoadAsync(); await StartSelectedJobAsync(); } }
    private async void Cancel_Click(object sender, RoutedEventArgs e) { _runCancellation?.Cancel(); await ChangeAsync(JobStatus.Cancelled); }
    private void Job_DoubleClick(object sender, MouseButtonEventArgs e) { if (SelectedJob is not null) new JobItemsWindow(_databasePath, SelectedJob.Id) { Owner = this }.ShowDialog(); }
}
