using System.Collections.ObjectModel;
using System.Windows;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
namespace TelegramControlStudio.App;
public partial class JobItemsWindow : Window
{
    private readonly string _databasePath; private readonly Guid _jobId; public ObservableCollection<JobItem> Items { get; } = [];
    public JobItemsWindow(string databasePath, Guid jobId) { InitializeComponent(); _databasePath = databasePath; _jobId = jobId; DataContext = this; Loaded += async (_, _) => { foreach (var item in await new JobItemRepository(_databasePath).ListAsync(_jobId)) Items.Add(item); }; }
}

