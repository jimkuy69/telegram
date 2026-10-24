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
    private async void Pause_Click(object sender, RoutedEventArgs e) => await ChangeAsync(JobStatus.Paused);
    private async void Resume_Click(object sender, RoutedEventArgs e) => await ChangeAsync(JobStatus.Queued);
    private async void Retry_Click(object sender, RoutedEventArgs e) { if (SelectedJob is not null && await new JobRepository(_databasePath).IncrementRetryAsync(SelectedJob.Id, "Manually retried from queue")) { await new ActivityLogRepository(_databasePath).AppendAsync(new ActivityLog(_accountId, SelectedJob.Id, "Info", "JobRetry", $"Job {SelectedJob.Id} manually retried from Queue UI.", DateTimeOffset.UtcNow)); await LoadAsync(); } }
    private async void Cancel_Click(object sender, RoutedEventArgs e) => await ChangeAsync(JobStatus.Cancelled);
    private void Job_DoubleClick(object sender, MouseButtonEventArgs e) { if (SelectedJob is not null) new JobItemsWindow(_databasePath, SelectedJob.Id) { Owner = this }.ShowDialog(); }
}

