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
    private readonly List<ContactRow> _allContactRows = [];
    private string? _contactFilter;
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
    public string SystemStatus => "Local SQLite • TDLib Native • ส่งจริงเฉพาะงานที่ผ่าน Consent และยืนยัน";
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

    public int TotalContactsCount => _allContactRows.Count;
    public int ConsentContactsCount => _allContactRows.Count(contact => contact.Consent == "ได้รับความยินยอม");
    public int NotAllowedContactsCount => _allContactRows.Count(contact => contact.Consent == "ยังไม่อนุญาต");

    public string AllContactsSummary => $"ทั้งหมด ({TotalContactsCount})";
    public string ConsentedContactsSummary => $"ได้รับความยินยอม ({ConsentContactsCount})";
    public string NotAllowedContactsSummary => $"ยังไม่อนุญาต ({NotAllowedContactsCount})";
    public string SendAvailabilityNotice => ActiveAccount is null
        ? "ยังไม่มีบัญชีที่เลือก • เลือกบัญชีและเข้าสู่ระบบก่อนสร้างงาน"
        : (string.Equals(ActiveAccount.Status, "Ready", StringComparison.OrdinalIgnoreCase) || string.Equals(ActiveAccount.Status, "Authenticated", StringComparison.OrdinalIgnoreCase))
            ? "พร้อมส่งเมื่อผู้รับมี Consent และกดยืนยันงาน • ระบบยังไม่ส่งจนกว่าจะยืนยัน"
            : $"บัญชี {ActiveAccount.DisplayName} ยังไม่พร้อมส่ง • สถานะ: {ActiveAccount.Status}";

    public int ConsentCount => Contacts.Count(contact => contact.Selected && contact.Consent == "ได้รับความยินยอม");

    public int SelectedCount => Contacts.Count(contact => contact.Selected);
    public string SelectionSummary => $"เลือกผู้รับ {SelectedCount} รายการ • ผ่าน Consent {ConsentCount} รายการ";

    private string _messageDraft = string.Empty;
    private string _linkDraft = string.Empty;
    public string MessageDraft
    {
        get => _messageDraft;
        set { _messageDraft = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewText)); }
    }
    public string LinkDraft
    {
        get => _linkDraft;
        set { _linkDraft = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewText)); }
    }
    public string PreviewText => string.IsNullOrWhiteSpace(LinkDraft)
        ? (string.IsNullOrWhiteSpace(MessageDraft) ? "ยังไม่มีข้อความสำหรับ Preview" : MessageDraft)
        : (string.IsNullOrWhiteSpace(MessageDraft) ? LinkDraft : $"{MessageDraft}\\n{LinkDraft}");

    public bool CanCreateJob => ActiveAccount is not null && ConsentCount > 0;

    public RelayCommand CreateJobCommand { get; }

    private string _errorSummary = "ข้อผิดพลาด: ยังไม่ได้โหลดข้อมูล";
    public string ErrorSummary
    {
        get => _errorSummary;
        private set { _errorSummary = value; OnPropertyChanged(); }
    }
    private string _dashboardSummary = "Dashboard: ยังไม่มีข้อมูล";
    public string DashboardSummary
    {
        get => _dashboardSummary;
        private set { _dashboardSummary = value; OnPropertyChanged(); }
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
        _allContactRows.Clear();
        foreach (var contact in contacts)
        {
            var row = new ContactRow(false, $"{contact.FirstName} {contact.LastName}".Trim(), contact.TelegramUserId, contact.Username is null ? string.Empty : $"@{contact.Username.TrimStart('@')}", contact.IsOptedOut ? "Opt-out" : contact.HasConsent ? "ได้รับความยินยอม" : "ยังไม่อนุญาต");
            row.PropertyChanged += ContactRow_PropertyChanged;
            _allContactRows.Add(row);
        }
        ApplyContactFilter();
        OnPropertyChanged(nameof(TotalContactsCount));
        OnPropertyChanged(nameof(ConsentContactsCount));
        OnPropertyChanged(nameof(NotAllowedContactsCount));
        OnPropertyChanged(nameof(AllContactsSummary));
        OnPropertyChanged(nameof(ConsentedContactsSummary));
        OnPropertyChanged(nameof(NotAllowedContactsSummary));
        OnPropertyChanged(nameof(SendAvailabilityNotice));
        OnPropertyChanged(nameof(ConsentCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CanCreateJob));
        CreateJobCommand.Refresh();
    }

    public void SetContactFilter(string? filter)
    {
        _contactFilter = filter;
        ApplyContactFilter();
    }

    private void ApplyContactFilter()
    {
        var visible = string.IsNullOrWhiteSpace(_contactFilter) ? _allContactRows : _allContactRows.Where(contact => contact.Consent == _contactFilter);
        Contacts.Clear();
        foreach (var row in visible) Contacts.Add(row);
        OnPropertyChanged(nameof(TotalContactsCount));
        OnPropertyChanged(nameof(ConsentContactsCount));
        OnPropertyChanged(nameof(NotAllowedContactsCount));
        OnPropertyChanged(nameof(AllContactsSummary));
        OnPropertyChanged(nameof(ConsentedContactsSummary));
        OnPropertyChanged(nameof(NotAllowedContactsSummary));
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
        DashboardSummary = $"Dashboard: ทั้งหมด {metrics.TotalJobs} งาน • สำเร็จ {metrics.CompletedJobs} • ล้มเหลว {metrics.FailedJobs} • รอคิว {metrics.QueuedJobs} • FloodWait {metrics.FloodWaitJobs}";
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
        if (ConsentCount == 0)
        {
            DataSourceNotice = "ยังสร้าง Preview ไม่ได้: กรุณาเลือกผู้รับที่มี Consent";
            return;
        }
        if (string.IsNullOrWhiteSpace(MessageDraft))
        {
            DataSourceNotice = "ยังสร้าง Preview ไม่ได้: กรุณากรอกข้อความก่อน";
            return;
        }
        var linkNotice = string.IsNullOrWhiteSpace(LinkDraft) ? string.Empty : " พร้อมลิงก์";
        DataSourceNotice = $"Preview พร้อมตรวจสอบ • ผู้รับ {ConsentCount} รายการ{linkNotice} • ยังไม่ส่งข้อความจริง";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
