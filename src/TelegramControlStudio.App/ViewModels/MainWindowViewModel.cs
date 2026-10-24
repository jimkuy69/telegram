using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ContactRow> Contacts { get; } =
    [
        new(true, "ตัวอย่างผู้รับ A", 100000001, "@example_a", "ได้รับความยินยอม"),
        new(false, "ตัวอย่างผู้รับ B", 100000002, "@example_b", "ยังไม่อนุญาต"),
        new(true, "ตัวอย่างผู้รับ C", 100000003, "@example_c", "ได้รับความยินยอม")
    ];

    public ObservableCollection<Account> Accounts { get; } = [];
    public ObservableCollection<string> ActivityLogs { get; } = [];
    private Account? _activeAccount;
    public Account? ActiveAccount
    {
        get => _activeAccount;
        set
        {
            _activeAccount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCreateJob));
            CreateJobCommand?.Refresh();
        }
    }

    private string _dataSourceNotice = "ข้อมูลตัวอย่างในเครื่องเท่านั้น • ยังไม่ได้ Sync Telegram";
    public string DataSourceNotice
    {
        get => _dataSourceNotice;
        private set { _dataSourceNotice = value; OnPropertyChanged(); }
    }

    private string _queueSummary = "Queue: ยังไม่มีงาน";
    public string QueueSummary
    {
        get => _queueSummary;
        private set { _queueSummary = value; OnPropertyChanged(); }
    }

    public int ConsentCount => Contacts.Count(contact => contact.Selected && contact.Consent == "ได้รับความยินยอม");

    public bool CanCreateJob => ActiveAccount is not null && ConsentCount > 0;

    public RelayCommand CreateJobCommand { get; }

    public async Task LoadContactsAsync(string databasePath, long accountId, CancellationToken cancellationToken = default)
    {
        var repository = new ContactRepository(databasePath);
        var contacts = await repository.ListAsync(accountId, cancellationToken);
        Contacts.Clear();
        foreach (var contact in contacts)
        {
            Contacts.Add(new ContactRow(
                contact.HasConsent && !contact.IsOptedOut,
                $"{contact.FirstName} {contact.LastName}".Trim(),
                contact.TelegramUserId,
                contact.Username is null ? string.Empty : $"@{contact.Username.TrimStart('@')}",
                contact.IsOptedOut ? "Opt-out" : contact.HasConsent ? "ได้รับความยินยอม" : "ยังไม่อนุญาต"));
        }
        OnPropertyChanged(nameof(ConsentCount));
        OnPropertyChanged(nameof(CanCreateJob));
        CreateJobCommand.Refresh();
    }

    public async Task LoadAccountsAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        var repository = new AccountRepository(databasePath);
        var accounts = await repository.ListAsync(cancellationToken);
        Accounts.Clear();
        foreach (var account in accounts)
            Accounts.Add(account);
        ActiveAccount = Accounts.FirstOrDefault();
        OnPropertyChanged(nameof(ActiveAccount));
        OnPropertyChanged(nameof(CanCreateJob));
        CreateJobCommand?.Refresh();
    }

    public async Task LoadQueueSummaryAsync(string databasePath, long accountId, CancellationToken cancellationToken = default)
    {
        var counts = await new JobRepository(databasePath).CountByStatusAsync(accountId, cancellationToken);
        QueueSummary = $"Queue: {counts.GetValueOrDefault(JobStatus.Queued)} รอคิว • {counts.GetValueOrDefault(JobStatus.Running)} กำลังทำ • {counts.GetValueOrDefault(JobStatus.WaitingForFloodWait)} รอ FloodWait";
    }

    public async Task LoadActivityLogsAsync(string databasePath, long accountId, CancellationToken cancellationToken = default)
    {
        var entries = await new ActivityLogRepository(databasePath).ListAsync(accountId, limit: 8, cancellationToken: cancellationToken);
        ActivityLogs.Clear();
        foreach (var entry in entries)
            ActivityLogs.Add($"{entry.CreatedAt:HH:mm} • {entry.EventType}: {entry.Message}");
    }

    public MainWindowViewModel()
    {
        CreateJobCommand = new RelayCommand(CreateJob, () => CanCreateJob);
    }

    private void CreateJob()
    {
        // Intentionally remains a local preview boundary until TDLib and confirmation gates exist.
        DataSourceNotice = $"พร้อมสร้าง Job แบบ Preview สำหรับ {ConsentCount} ผู้รับที่ยินยอม";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

