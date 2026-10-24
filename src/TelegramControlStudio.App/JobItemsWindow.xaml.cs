using System.Collections.ObjectModel;
using System.Windows;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
namespace TelegramControlStudio.App;
public partial class JobItemsWindow : Window
{
    private readonly string _databasePath; private readonly Guid _jobId; public ObservableCollection<JobItem> Items { get; } = []; public JobItem? SelectedItem { get; set; }
    public JobItemsWindow(string databasePath, Guid jobId) { InitializeComponent(); _databasePath = databasePath; _jobId = jobId; DataContext = this; Loaded += async (_, _) => { foreach (var item in await new JobItemRepository(_databasePath).ListAsync(_jobId)) Items.Add(item); }; }
    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is null || !string.Equals(SelectedItem.Status, "Failed", StringComparison.OrdinalIgnoreCase)) return;
        if (await new JobItemRepository(_databasePath).UpdateStatusAsync(_jobId, SelectedItem.TargetId, "Queued", null, true))
        {
            await new ActivityLogRepository(_databasePath).AppendAsync(new ActivityLog(SelectedItem.AccountId, _jobId, "Info", "JobItemRetry", $"Target {SelectedItem.TargetId} manually queued for retry.", DateTimeOffset.UtcNow));
            Items.Clear(); foreach (var item in await new JobItemRepository(_databasePath).ListAsync(_jobId)) Items.Add(item);
        }
    }
}

