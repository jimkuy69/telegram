using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
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
            OnPropertyChanged(nameof(ActiveAccountStatus));
            OnPropertyChanged(nameof(CanCreateJob));
            CreateJobCommand?.Refresh();
        }
    }

    private string _dataSourceNotice = "ข้อมูลตัวอย่างในเครื่องเท่านั้น • ยังไม่ได้ Sync Telegram";
    public string SystemStatus => "Local SQLite • TDLib Native ตรวจสอบ hash แล้ว • Runtime/การส่งจริงยังปิดอยู่";
    public string ActiveAccountStatus => ActiveAccount is null
        ? "ยังไม่ได้เลือกบัญชี"
        : $"{ActiveAccount.DisplayName} • {ActiveAccount.Status}";
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

    private string _pendingRequestsSummary = "คำขอเข้าร่วม: 0 รายการ";
    public string PendingRequestsSummary
    {
        get => _pendingRequestsSummary;
        private set { _pendingRequestsSummary = value; OnPropertyChanged(); }
    }

    public int ConsentCount => Contacts.Count(contact => contact.Selected && contact.Consent == "ได้รับความยินยอม");

    public int SelectedCount => Contacts.Count(contact => contact.Selected);
    public string SelectionSummary => $"เลือกผู้รับ {SelectedCount} รายการ • ผ่าน Consent {ConsentCount} รายการ";

    public bool CanCreateJob => ActiveAccount is not null && ConsentCount > 0;

    public RelayCommand CreateJobCommand { get; }

    private string _errorSummary = "ข้อผิดพลาด: ยังไม่ได้โหลดข้อมูล";
    public string ErrorSummary
    {
        get => _errorSummary;
        private set { _errorSummary = value; OnPropertyChanged(); }
    }
    private string _campaignSummary = "Campaign: ยังไม่มีงาน";
    public string CampaignSummary
    {
        get => _campaignSummary;
        private set { _campaignSummary = value; OnPropertyChanged(); }
    }

    public async Task LoadContactsAsync(string databasePath, long accountId, CancellationToken cancellationToken = default)
    {
        var repository = new ContactRepository(databasePath);
        ReplaceContacts(repository, await repository.ListAsync(accountId, cancellationToken));
    }

    public async Task SearchContactsAsync(string databasePath, long accountId, string? query, CancellationToken cancellationToken = default)
    {
        var repository = new ContactRepository(databasePath);
        ReplaceContacts(repository, await repository.SearchAsync(accountId, query, cancellationToken));
    }

    private void ReplaceContacts(ContactRepository repository, IReadOnlyList<Contact> contacts)
    {
        Contacts.Clear();
        foreach (var contact in contacts)
        {
            var row = new ContactRow(
                contact.HasConsent && !contact.IsOptedOut,
                $"{contact.FirstName} {contact.LastName}".Trim(),
                contact.TelegramUserId,
                contact.Username is null ? string.Empty : $"@{contact.Username.TrimStart('@')}",
                contact.IsOptedOut ? "Opt-out" : contact.HasConsent ? "ได้รับความยินยอม" : "ยังไม่อนุญาต");
            row.PropertyChanged += ContactRow_PropertyChanged;
            Contacts.Add(row);
        }
        OnPropertyChanged(nameof(ConsentCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CanCreateJob));
        CreateJobCommand.Refresh();
    }

    private void ContactRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ContactRow.Selected)) return;
        OnPropertyChanged(nameof(ConsentCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
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
        var jobs = await new JobRepository(databasePath).ListAsync(accountId, cancellationToken);
        var synchronizer = new JobAggregateSynchronizer(databasePath);
        foreach (var job in jobs) await synchronizer.SynchronizeAsync(job.Id, cancellationToken);
        var counts = await new JobRepository(databasePath).CountByStatusAsync(accountId, cancellationToken);
        var total = counts.Values.Sum();
        var metrics = await new DashboardMetricsRepository(databasePath).LoadAsync(accountId, cancellationToken: cancellationToken);
        ErrorSummary = $"เหตุการณ์ผิดพลาดทั้งหมด • สิทธิ์: {metrics.PermissionErrors} • Privacy: {metrics.PrivacyErrors} • Network: {metrics.NetworkErrors}";
        CampaignSummary = $"Campaign: {metrics.CampaignJobs} งาน • สำเร็จ {metrics.CampaignCompletedJobs} • ล้มเหลว {metrics.CampaignFailedJobs}";
        QueueSummary = $"Queue: {total} งานทั้งหมด • {counts.GetValueOrDefault(JobStatus.Queued)} รอคิว • {counts.GetValueOrDefault(JobStatus.Running)} กำลังทำ • {counts.GetValueOrDefault(JobStatus.WaitingForFloodWait)} รอ FloodWait • {counts.GetValueOrDefault(JobStatus.Completed)} สำเร็จ • {counts.GetValueOrDefault(JobStatus.Failed)} ล้มเหลว • {counts.GetValueOrDefault(JobStatus.Cancelled)} ยกเลิก";
    }

    public async Task LoadActivityLogsAsync(string databasePath, long accountId, CancellationToken cancellationToken = default)
    {
        var entries = await new ActivityLogRepository(databasePath).ListAsync(accountId, limit: 8, cancellationToken: cancellationToken);
        ActivityLogs.Clear();
        foreach (var entry in entries)
            ActivityLogs.Add($"{entry.CreatedAt:HH:mm} • {entry.EventType}: {entry.Message}");
    }

    public async Task LoadPendingRequestsAsync(string databasePath, long accountId, CancellationToken cancellationToken = default)
    {
        var requests = await new JoinRequestRepository(databasePath).ListPendingAsync(accountId, cancellationToken: cancellationToken);
        PendingRequestsSummary = $"คำขอเข้าร่วม: {requests.Count} รายการรอการตรวจสอบ";
    }

    public async Task<string> DecideJoinRequestAsync(
        string databasePath,
        long accountId,
        long chatId,
        long telegramUserId,
        string decision,
        string reason,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        var validation = JoinRequestDecisionPolicy.Validate(decision, reason, confirmed);
        if (!string.IsNullOrEmpty(validation)) return validation;
        var changed = await new JoinRequestRepository(databasePath).DecideAsync(accountId, chatId, telegramUserId, decision, reason, cancellationToken);
        if (changed) await LoadPendingRequestsAsync(databasePath, accountId, cancellationToken);
        return changed ? "OK" : "REQUEST_NOT_PENDING";
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

