using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    public const int PageSize = PagingPolicy.DefaultPageSize;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ContactRow> Contacts { get; } =
    [
        new(true, "ตัวอย่างผู้รับ A", 100000001, "@example_a", ContactConsentState.Consented),
        new(false, "ตัวอย่างผู้รับ B", 100000002, "@example_b", ContactConsentState.NotAllowed),
        new(true, "ตัวอย่างผู้รับ C", 100000003, "@example_c", ContactConsentState.Consented)
    ];

    public ObservableCollection<Account> Accounts { get; } = [];
    public ObservableCollection<string> ActivityLogs { get; } = [];
    public ObservableCollection<PageNumberItem> PageNumbers { get; } = [];

    private Account? _activeAccount;
    private readonly List<ContactRow> _allContactRows = [];
    private List<ContactRow> _filteredRows = [];
    private string? _contactFilter;
    private string _sortMode = "name";
    private int _currentPage = 1;

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
    public int ConsentContactsCount => _allContactRows.Count(contact => contact.ConsentState == ContactConsentState.Consented);
    public int NotAllowedContactsCount => _allContactRows.Count(contact => contact.ConsentState == ContactConsentState.NotAllowed);
    public int OptedOutContactsCount => _allContactRows.Count(contact => contact.ConsentState == ContactConsentState.OptedOut);

    public string AllContactsSummary => $"ทั้งหมด ({TotalContactsCount})";
    public string ConsentedContactsSummary => $"ได้รับความยินยอม ({ConsentContactsCount})";
    public string NotAllowedContactsSummary => $"ยังไม่อนุญาต ({NotAllowedContactsCount})";
    public string OptedOutContactsSummary => $"Opt-out ({OptedOutContactsCount})";

    public string SendAvailabilityNotice => ActiveAccount is null
        ? "ยังไม่มีบัญชีที่เลือก • เลือกบัญชีและเข้าสู่ระบบก่อนสร้างงาน"
        : (string.Equals(ActiveAccount.Status, "Ready", StringComparison.OrdinalIgnoreCase) || string.Equals(ActiveAccount.Status, "Authenticated", StringComparison.OrdinalIgnoreCase))
            ? "พร้อมส่งเมื่อผู้รับมี Consent และกดยืนยันงาน • ระบบยังไม่ส่งจนกว่าจะยืนยัน"
            : $"บัญชี {ActiveAccount.DisplayName} ยังไม่พร้อมส่ง • สถานะ: {ActiveAccount.Status}";

    public int ConsentCount => _allContactRows.Count(contact => contact.Selected && contact.ConsentState == ContactConsentState.Consented);
    public int SelectedCount => _allContactRows.Count(contact => contact.Selected);
    public int SelectedConsentCount => ConsentCount;
    public int SelectedNotAllowedCount => _allContactRows.Count(contact => contact.Selected && contact.ConsentState == ContactConsentState.NotAllowed);
    public int SelectedOptOutCount => _allContactRows.Count(contact => contact.Selected && contact.ConsentState == ContactConsentState.OptedOut);

    public bool HasSelection => SelectedCount > 0;
    public bool HasConsentedSelection => ConsentCount > 0;
    public bool IsEmpty => Contacts.Count == 0;
    public bool? PageSelectionState
    {
        get
        {
            if (Contacts.Count == 0) return false;
            var selectedOnPage = Contacts.Count(contact => contact.Selected);
            if (selectedOnPage == 0) return false;
            return selectedOnPage == Contacts.Count ? true : null;
        }
    }

    public IReadOnlyList<ContactRow> SelectedRows =>
        _allContactRows.Where(contact => contact.Selected).ToArray();

    public IReadOnlyList<ContactRow> SelectedPreviewChips => SelectedRows.Take(2).ToArray();
    public string SelectedChipsSummary => $"ผู้รับที่เลือก ({SelectedCount} ราย)";

    public string SelectionSummary => $"เลือกแล้ว {SelectedCount} รายชื่อ • ผ่าน Consent {ConsentCount} รายชื่อ";

    public int CurrentPage => _currentPage;
    public int TotalPages => PagingPolicy.TotalPages(_filteredRows.Count, PageSize);
    public string PageSummary
    {
        get
        {
            if (_filteredRows.Count == 0) return "ไม่มีรายชื่อในตัวกรองนี้";
            var (skip, take) = PagingPolicy.Slice(_currentPage, PageSize, _filteredRows.Count);
            return $"แสดง {skip + 1}–{skip + take} จาก {_filteredRows.Count} รายชื่อ";
        }
    }

    private string _messageDraft = string.Empty;
    private string _linkDraft = string.Empty;
    public const int MessageLimit = 4096;

    public string MessageDraft
    {
        get => _messageDraft;
        set
        {
            _messageDraft = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewText));
            OnPropertyChanged(nameof(PreviewMessage));
            OnPropertyChanged(nameof(MessageCounter));
        }
    }

    public string LinkDraft
    {
        get => _linkDraft;
        set
        {
            _linkDraft = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewText));
            OnPropertyChanged(nameof(PreviewLink));
            OnPropertyChanged(nameof(HasPreviewLink));
        }
    }

    public string MessageCounter => $"{MessageDraft.Length}/{MessageLimit}";

    public string PreviewMessage => string.IsNullOrWhiteSpace(MessageDraft)
        ? "พิมพ์ข้อความเพื่อดูตัวอย่างก่อนส่ง"
        : MessageDraft;

    public string PreviewLink => LinkDraft;
    public bool HasPreviewLink => !string.IsNullOrWhiteSpace(LinkDraft);

    public string PreviewText => string.IsNullOrWhiteSpace(LinkDraft)
        ? (string.IsNullOrWhiteSpace(MessageDraft) ? "ยังไม่มีข้อความสำหรับ Preview" : MessageDraft)
        : string.IsNullOrWhiteSpace(MessageDraft)
            ? LinkDraft
            : $"{MessageDraft}{Environment.NewLine}{LinkDraft}";

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
        ReplaceContacts(await repository.ListAsync(accountId, cancellationToken));
    }

    public async Task SearchContactsAsync(string databasePath, long accountId, string? query, CancellationToken cancellationToken = default)
    {
        var repository = new ContactRepository(databasePath);
        ReplaceContacts(await repository.SearchAsync(accountId, query, cancellationToken));
    }

    private void ReplaceContacts(IReadOnlyList<Contact> contacts)
    {
        _allContactRows.Clear();
        foreach (var contact in contacts)
        {
            var state = ContactConsentPolicy.Classify(contact.HasConsent, contact.IsOptedOut);
            var row = new ContactRow(false, $"{contact.FirstName} {contact.LastName}".Trim(), contact.TelegramUserId, contact.Username is null ? string.Empty : $"@{contact.Username.TrimStart('@')}", state);
            row.PropertyChanged += ContactRow_PropertyChanged;
            _allContactRows.Add(row);
        }
        ApplyContactFilter();
        NotifyCounts();
        OnPropertyChanged(nameof(SendAvailabilityNotice));
    }

    public void SetContactFilter(string? filter)
    {
        _contactFilter = filter;
        ApplyContactFilter();
    }

    public void SetSort(string? mode)
    {
        var normalized = string.Equals(mode, "consent", StringComparison.OrdinalIgnoreCase) ? "consent" : "name";
        if (_sortMode == normalized) return;
        _sortMode = normalized;
        _currentPage = 1;
        SortFilteredRows();
        ApplyPage();
    }

    public void GoToPage(int page)
    {
        var clamped = PagingPolicy.Clamp(page, TotalPages);
        if (clamped == _currentPage) return;
        _currentPage = clamped;
        ApplyPage();
    }

    public void NextPage() => GoToPage(_currentPage + 1);
    public void PreviousPage() => GoToPage(_currentPage - 1);

    public void SelectAllFiltered()
    {
        foreach (var row in _filteredRows) row.Selected = true;
        NotifyCounts();
    }

    public void ClearSelection()
    {
        foreach (var row in _allContactRows) row.Selected = false;
        NotifyCounts();
    }

    public void ClearPageSelection()
    {
        foreach (var row in Contacts) row.Selected = false;
        NotifyCounts();
    }

    private void ApplyContactFilter()
    {
        _filteredRows = string.IsNullOrWhiteSpace(_contactFilter)
            ? _allContactRows.ToList()
            : _allContactRows.Where(contact => contact.Consent == _contactFilter).ToList();
        _currentPage = 1;
        SortFilteredRows();
        ApplyPage();
    }

    private void SortFilteredRows()
    {
        if (_sortMode == "consent")
        {
            _filteredRows.Sort((left, right) =>
            {
                var rank = ContactConsentPolicy.SortRank(left.ConsentState).CompareTo(ContactConsentPolicy.SortRank(right.ConsentState));
                return rank != 0 ? rank : string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            });
        }
        else
        {
            _filteredRows.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    private void ApplyPage()
    {
        var totalPages = TotalPages;
        if (_currentPage > totalPages) _currentPage = totalPages;
        if (_currentPage < 1) _currentPage = 1;

        var (skip, take) = PagingPolicy.Slice(_currentPage, PageSize, _filteredRows.Count);
        Contacts.Clear();
        foreach (var row in _filteredRows.Skip(skip).Take(take))
            Contacts.Add(row);

        RebuildPageNumbers(totalPages);
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageSummary));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(PageSelectionState));
    }

    private void RebuildPageNumbers(int totalPages)
    {
        PageNumbers.Clear();
        foreach (var page in PagingPolicy.Window(_currentPage, totalPages))
            PageNumbers.Add(new PageNumberItem(page, page == _currentPage));
    }

    private void ContactRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ContactRow.Selected)) return;
        NotifyCounts();
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(TotalContactsCount));
        OnPropertyChanged(nameof(ConsentContactsCount));
        OnPropertyChanged(nameof(NotAllowedContactsCount));
        OnPropertyChanged(nameof(OptedOutContactsCount));
        OnPropertyChanged(nameof(AllContactsSummary));
        OnPropertyChanged(nameof(ConsentedContactsSummary));
        OnPropertyChanged(nameof(NotAllowedContactsSummary));
        OnPropertyChanged(nameof(OptedOutContactsSummary));
        OnPropertyChanged(nameof(ConsentCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedConsentCount));
        OnPropertyChanged(nameof(SelectedNotAllowedCount));
        OnPropertyChanged(nameof(SelectedOptOutCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(SelectedRows));
        OnPropertyChanged(nameof(SelectedPreviewChips));
        OnPropertyChanged(nameof(SelectedChipsSummary));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasConsentedSelection));
        OnPropertyChanged(nameof(PageSelectionState));
        OnPropertyChanged(nameof(CanCreateJob));
        CreateJobCommand?.Refresh();
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
        _filteredRows = _allContactRows.ToList();
        ApplyPage();
        NotifyCounts();
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
