using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
namespace TelegramControlStudio.App;
public partial class QueueWindow : Window
{
    private readonly string _databasePath; private readonly long _accountId;
    public ObservableCollection<Job> Jobs { get; } = []; public Job? SelectedJob { get; set; }
    public QueueWindow(string databasePath, long accountId) { InitializeComponent(); _databasePath = databasePath; _accountId = accountId; DataContext = this; Loaded += async (_, _) => await LoadAsync(); }
    private async Task LoadAsync()
    {
        var jobs = await new JobRepository(_databasePath).ListAsync(_accountId);
        var synchronizer = new JobAggregateSynchronizer(_databasePath);
        foreach (var job in jobs) await synchronizer.SynchronizeAsync(job.Id);
        Jobs.Clear();
        foreach (var job in await new JobRepository(_databasePath).ListAsync(_accountId)) Jobs.Add(job);
    }
    private async Task ChangeAsync(JobStatus status)
    {
        if (SelectedJob is null) return;
        if (await new JobRepository(_databasePath).UpdateStatusAsync(SelectedJob.Id, status))
        {
            await new ActivityLogRepository(_databasePath).AppendAsync(new ActivityLog(_accountId, SelectedJob.Id, "Info", $"Job{status}", $"Job {SelectedJob.Id} changed to {status} from Queue UI.", DateTimeOffset.UtcNow));
        }
        await LoadAsync();
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJob is null)
        {
            MessageBox.Show("กรุณาเลือก Job ก่อน", "ยังไม่ได้เลือก", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (SelectedJob.Status != JobStatus.WaitingForConfirmation)
        {
            MessageBox.Show("Job นี้ไม่ได้อยู่ในสถานะรอยืนยัน", "ยืนยันไม่ได้", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show("ยืนยันนำ Job นี้เข้าคิวหรือไม่? ระบบยังไม่ส่งข้อความจริง", "ยืนยันเข้าคิว", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        await new JobItemRepository(_databasePath).UpdateStatusForJobAsync(
            SelectedJob.Id,
            JobStatus.WaitingForConfirmation.ToString(),
            JobStatus.Queued.ToString());
        await ChangeAsync(JobStatus.Queued);
    }

    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJob is null || SelectedJob.Status != JobStatus.Running)
        {
            MessageBox.Show("เลือก Job ที่กำลังทำงานอยู่ก่อนกดหยุดชั่วคราว", "หยุดชั่วคราวไม่ได้", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await ChangeAsync(JobStatus.Paused);
    }

    private async void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJob is null)
        {
            MessageBox.Show("กรุณาเลือก Job ก่อน", "ยังไม่ได้เลือก", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (SelectedJob.Status is not (JobStatus.Paused or JobStatus.WaitingForFloodWait))
        {
            MessageBox.Show("Resume ใช้ได้กับ Job ที่ Paused หรือรอ FloodWait เท่านั้น", "Resume ไม่ได้", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await ChangeAsync(JobStatus.Queued);
    }
    private async void Retry_Click(object sender, RoutedEventArgs e) { if (SelectedJob is not null && await new JobRepository(_databasePath).IncrementRetryAsync(SelectedJob.Id, "Manually retried from queue")) { await new ActivityLogRepository(_databasePath).AppendAsync(new ActivityLog(_accountId, SelectedJob.Id, "Info", "JobRetry", $"Job {SelectedJob.Id} manually retried from Queue UI.", DateTimeOffset.UtcNow)); await LoadAsync(); } }
    private async void Cancel_Click(object sender, RoutedEventArgs e) => await ChangeAsync(JobStatus.Cancelled);
    private void Job_DoubleClick(object sender, MouseButtonEventArgs e) { if (SelectedJob is not null) new JobItemsWindow(_databasePath, SelectedJob.Id) { Owner = this }.ShowDialog(); }
}

