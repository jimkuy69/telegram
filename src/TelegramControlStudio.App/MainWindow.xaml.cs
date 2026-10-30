using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.IO;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.App.ViewModels;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Safety;
using TelegramControlStudio.Core.Export;
using TelegramControlStudio.Infrastructure.Telegram;
using TelegramControlStudio.Core.Security;
using System.Text;
using System.Security.Cryptography;

namespace TelegramControlStudio.App;

public partial class MainWindow : Window
{
    private readonly string _databasePath = AppPaths.ResolveDatabasePath();
    private readonly DispatcherTimer _autoRunTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainWindowViewModel();
        DataContext = viewModel;
        _ = LoadAccountsAsync(viewModel, _databasePath);
        _ = UpdateSafetyBadgeAsync();
        Activated += async (_, _) => await UpdateSafetyBadgeAsync();
        _autoRunTimer.Tick += async (_, _) => await UpdateSafetyBadgeAsync();
        _autoRunTimer.Start();
    }

    private async Task UpdateSafetyBadgeAsync()
    {
        try
        {
            var settings = await new SendSafetyRepository(_databasePath).GetAsync();
            if (settings.KillSwitchEngaged)
            {
                SafetyBadge.Background = (System.Windows.Media.Brush)FindResource("DangerSoft");
                SafetyBadge.BorderBrush = (System.Windows.Media.Brush)FindResource("Danger");
                SafetyBadgeText.Foreground = (System.Windows.Media.Brush)FindResource("DangerText");
                SafetyBadgeText.Text = "KILL SWITCH";
                SafetyBadge.ToolTip = "หยุดฉุกเฉินอยู่: ระบบบล็อกการส่งทั้งหมด";
            }
            else if (settings.TestModeEnabled)
            {
                SafetyBadge.Background = (System.Windows.Media.Brush)FindResource("WarningSoft");
                SafetyBadge.BorderBrush = (System.Windows.Media.Brush)FindResource("Warning");
                SafetyBadgeText.Foreground = (System.Windows.Media.Brush)FindResource("WarningText");
                SafetyBadgeText.Text = $"TEST MODE ({settings.AllowlistUserIds.Count})";
                SafetyBadge.ToolTip = "โหมดทดสอบ: ส่งได้เฉพาะ Telegram User ID ใน allowlist";
            }
            else
            {
                SafetyBadge.Background = (System.Windows.Media.Brush)FindResource("SuccessSoft");
                SafetyBadge.BorderBrush = (System.Windows.Media.Brush)FindResource("Success");
                SafetyBadgeText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessText");
                SafetyBadgeText.Text = "LIVE";
                SafetyBadge.ToolTip = "โหมดใช้งานจริง: ส่งได้ทุกคนที่ให้ Consent";
            }
            UpdateAutoRunBadge(settings);
        }
        catch
        {
            SafetyBadgeText.Text = "SAFETY ?";
        }
    }

    private void UpdateAutoRunBadge(SendSafetySettings settings)
    {
        var app = (App)Application.Current;
        var connected = app.TdLibClients.Count;
        var running = app.JobGate.Count;
        string text;
        string brush;
        if (settings.KillSwitchEngaged)
        {
            text = "AUTO-RUN: หยุดฉุกเฉิน";
            brush = "DangerText";
        }
        else if (connected == 0)
        {
            text = "AUTO-RUN: รอเข้าสู่ระบบ";
            brush = "TextMuted";
        }
        else if (running > 0)
        {
            text = $"AUTO-RUN: กำลังส่ง {running}";
            brush = "SuccessText";
        }
        else
        {
            text = $"AUTO-RUN: พร้อม ({connected})";
            brush = "SuccessText";
        }
        AutoRunBadgeText.Text = text;
        AutoRunBadgeText.Foreground = (System.Windows.Media.Brush)FindResource(brush);
        AutoRunBadge.ToolTip = connected == 0
            ? "ตัวรันอัตโนมัติจะทำงานหลังคุณเข้าสู่ระบบ Telegram ในหน้าต่างนี้"
            : "งานที่ยืนยันแล้วในคิวจะถูกรันเอง และงานที่รอ FloodWait จะไปต่อเมื่อครบเวลา";
    }

    private async void EmergencyStop_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("ยืนยันหยุดฉุกเฉิน? ระบบจะหยุดส่งทันที และบล็อกการส่งใหม่ทั้งหมดจนกว่าจะปลดล็อก", "ยืนยันหยุดฉุกเฉิน", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await new SendSafetyRepository(_databasePath).SetKillSwitchAsync(true);
            EmergencyStopSignal.Trigger();
            await new ActivityLogRepository(_databasePath).AppendAsync(new ActivityLog(
                (DataContext as MainWindowViewModel)?.ActiveAccount?.Id ?? 0,
                null,
                "Warning",
                "EmergencyStopEngaged",
                "Kill switch engaged from the main window.",
                DateTimeOffset.UtcNow));
            await UpdateSafetyBadgeAsync();
            MessageBox.Show("หยุดฉุกเฉินแล้ว งานที่กำลังส่งจะถูกหยุดชั่วคราว รายการที่ยังไม่ส่งยังอยู่ในคิว", "หยุดฉุกเฉิน", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"หยุดฉุกเฉินไม่สำเร็จ\n{exception.Message}", "ผิดพลาด", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static async Task LoadAccountsAsync(MainWindowViewModel viewModel, string databasePath)
    {
        try
        {
            await new SqliteDatabase(databasePath).InitializeAsync();
            await viewModel.LoadAccountsAsync(databasePath);
            await RecoverJobsAsync(databasePath, viewModel.Accounts);
            if (viewModel.ActiveAccount is not null)
                await viewModel.LoadQueueSummaryAsync(databasePath, viewModel.ActiveAccount.Id);
            if (viewModel.ActiveAccount is not null)
                await viewModel.LoadActivityLogsAsync(databasePath, viewModel.ActiveAccount.Id);
            if (viewModel.ActiveAccount is not null)
                await viewModel.LoadPendingRequestsAsync(databasePath, viewModel.ActiveAccount.Id);
            if (viewModel.ActiveAccount is not null)
                await viewModel.LoadContactsAsync(databasePath, viewModel.ActiveAccount.Id);
        }
        catch
        {
            // The dashboard remains usable before the first local account exists.
        }
    }

    private static async Task RecoverJobsAsync(string databasePath, IEnumerable<Account> accounts)
    {
        var jobs = new JobRepository(databasePath);
        var logs = new ActivityLogRepository(databasePath);
        foreach (var account in accounts)
        {
            await jobs.ResumeEligibleJobsAsync(account.Id, DateTimeOffset.UtcNow);
            var paused = await jobs.PauseRunningJobsAsync(account.Id);
            if (paused > 0)
            {
                await logs.AppendAsync(new ActivityLog(
                    account.Id,
                    null,
                    "Info",
                    "SafeStartupRecovery",
                    $"Paused {paused} running job(s) during startup recovery.",
                    DateTimeOffset.UtcNow));
            }
        }
    }

    private static async Task LoadContactsAsync(MainWindowViewModel viewModel, string databasePath)
    {
        try
        {
            await viewModel.LoadContactsAsync(databasePath, accountId: 0);
        }
        catch
        {
            // Startup must remain usable while no authenticated account exists.
        }
    }

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TelegramControlStudio",
            "telegram-control-studio.db");
        var wizard = new AddAccountWindow(databasePath) { Owner = this };
        if (wizard.ShowDialog() != true || wizard.CreatedAccountId is not long createdAccountId || DataContext is not MainWindowViewModel viewModel)
            return;

        await viewModel.LoadAccountsAsync(databasePath);
        viewModel.ActiveAccount = viewModel.Accounts.FirstOrDefault(account => account.Id == createdAccountId);
        Title = "Telegram Control Studio • บันทึกบัญชีแล้ว";
        new SettingsWindow(databasePath, createdAccountId) { Owner = this }.ShowDialog();
    }

    private void PendingRequests_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = _databasePath;
        new PendingRequestsWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void InviteLinks_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = _databasePath;
        new InviteLinksWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void ImportContacts_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = _databasePath;
        new ContactImportWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private async void SearchContacts_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null) return;
        var databasePath = _databasePath;
        try
        {
            await viewModel.SearchContactsAsync(databasePath, viewModel.ActiveAccount.Id, ContactSearchBox.Text);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"ค้นหาไม่สำเร็จ\n{exception.Message}", "ค้นหาไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RefreshContacts_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null) return;
        var databasePath = _databasePath;
        await viewModel.LoadContactsAsync(databasePath, viewModel.ActiveAccount.Id);
    }

    private async void SyncContacts_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var app = (App)Application.Current;
        if (!string.Equals(viewModel.ActiveAccount.Status, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("กรุณาเข้าสู่ระบบและยืนยันตัวตนกับ Telegram ให้สำเร็จก่อน Sync Contacts", "ยังไม่พร้อม Sync", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!app.TdLibClients.TryGet(viewModel.ActiveAccount.Id, out var client) || client is null)
        {
            MessageBox.Show("บัญชีนี้ยังไม่ได้เชื่อมต่อและยืนยันตัวตนกับ TDLib", "ยังไม่พร้อม Sync", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var databasePath = _databasePath;
            var contacts = await new TdLibContactSyncService(client, databasePath).SyncAsync(viewModel.ActiveAccount.Id);
            await viewModel.LoadContactsAsync(databasePath, viewModel.ActiveAccount.Id);
            await viewModel.LoadActivityLogsAsync(databasePath, viewModel.ActiveAccount.Id);
            MessageBox.Show($"Sync Contacts สำเร็จ {contacts.Count} รายการ", "Sync สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Sync Contacts ไม่สำเร็จ\n{exception.Message}", "Sync ไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private ContactRow[] GetCheckedContacts() =>
        DataContext is MainWindowViewModel viewModel ? viewModel.SelectedRows.ToArray() : [];

    private void SelectAllContacts_Click(object sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.SelectAllFiltered();
    private void ClearSelection_Click(object sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.ClearSelection();
    private void ClearPageSelection_Click(object sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.ClearPageSelection();
    private void PrevPage_Click(object sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.PreviousPage();
    private void NextPage_Click(object sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.NextPage();

    private void PageNumber_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: int page } && DataContext is MainWindowViewModel viewModel)
            viewModel.GoToPage(page);
    }

    private void Sort_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || SortBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
            return;
        viewModel.SetSort(item.Tag?.ToString());
    }

    private void ContactSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        SearchContacts_Click(sender, e);
        e.Handled = true;
    }

    private void FocusRecipients_Click(object sender, RoutedEventArgs e)
    {
        ContactSearchBox.Focus();
        Keyboard.Focus(ContactSearchBox);
    }

    private void ContinueToMessage_Click(object sender, RoutedEventArgs e) => RightTabs.SelectedIndex = 1;
    private void Review_Click(object sender, RoutedEventArgs e) => RightTabs.SelectedIndex = 2;
    private void BackToRecipients_Click(object sender, RoutedEventArgs e) => RightTabs.SelectedIndex = 0;
    private void BackToMessage_Click(object sender, RoutedEventArgs e) => RightTabs.SelectedIndex = 1;

    private void ClearLink_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.LinkDraft = string.Empty;
    }

    private void DeselectChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: ContactRow row }) return;
        row.Selected = false;
        ContactGrid.Items.Refresh();
    }

    private void ComposeMedia_Click(object sender, RoutedEventArgs e) => MediaLibrary_Click(sender, e);

    private void SaveDraftAsTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(viewModel.MessageDraft))
        {
            MessageBox.Show("กรุณากรอกข้อความก่อนบันทึกเป็นเทมเพลต", "ยังไม่มีข้อความ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = _databasePath;
        new TemplateEditorWindow(databasePath, viewModel.ActiveAccount.Id, viewModel.MessageDraft, viewModel.LinkDraft) { Owner = this }.ShowDialog();
    }

    private void SelectVisibleContacts_Click(object sender, RoutedEventArgs e)
    {
        if (ContactGrid.DataContext is not MainWindowViewModel viewModel) return;
        var checkbox = sender as System.Windows.Controls.CheckBox;
        var shouldSelect = checkbox?.IsChecked != null;
        foreach (var contact in viewModel.Contacts)
            contact.Selected = shouldSelect;
        ContactGrid.Items.Refresh();
        if (checkbox is not null)
            checkbox.IsChecked = shouldSelect;
    }

    private void ContactCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.DataContext is ContactRow row)
        {
            row.Selected = checkBox.IsChecked == true;
            ContactGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
            ContactGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        }
    }

    private async void DeleteSelectedContacts_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null) return;
        var selected = GetCheckedContacts();
        if (selected.Length == 0)
        {
            MessageBox.Show("กรุณาเลือกรายชื่อที่ต้องการลบ", "ยังไม่ได้เลือก", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var answer = MessageBox.Show($"ยืนยัน Soft Delete จำนวน {selected.Length} รายการ? ข้อมูลจะไม่ถูกลบถาวร", "ยืนยันการลบ", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        var databasePath = _databasePath;
        var repository = new ContactRepository(databasePath);
        var logs = new ActivityLogRepository(databasePath);
        foreach (var contact in selected)
        {
            if (await repository.SoftDeleteAsync(viewModel.ActiveAccount.Id, contact.Id))
            {
                await logs.AppendAsync(new ActivityLog(
                    viewModel.ActiveAccount.Id,
                    null,
                    "Info",
                    "ContactSoftDeleted",
                    $"Contact soft-deleted for telegram_user_id={contact.Id}.",
                    DateTimeOffset.UtcNow));
            }
        }
        await viewModel.LoadActivityLogsAsync(databasePath, viewModel.ActiveAccount.Id);
        await viewModel.LoadContactsAsync(databasePath, viewModel.ActiveAccount.Id);
    }

    private void ShowAllContacts_Click(object sender, RoutedEventArgs e) { if (DataContext is MainWindowViewModel vm) vm.SetContactFilter(null); }
    private void ShowConsentedContacts_Click(object sender, RoutedEventArgs e) { if (DataContext is MainWindowViewModel vm) vm.SetContactFilter("ได้รับความยินยอม"); }
    private void ShowOptedOutContacts_Click(object sender, RoutedEventArgs e) { if (DataContext is MainWindowViewModel vm) vm.SetContactFilter("Opt-out"); }
    private void ShowNotAllowedContacts_Click(object sender, RoutedEventArgs e) { if (DataContext is MainWindowViewModel vm) vm.SetContactFilter("ยังไม่อนุญาต"); }

    private async void SetConsent_Click(object sender, RoutedEventArgs e) => await UpdateSelectedPolicyAsync(true);

    private async void SetOptOut_Click(object sender, RoutedEventArgs e) => await UpdateSelectedPolicyAsync(false);

    private async Task UpdateSelectedPolicyAsync(bool consent)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null) return;
        var selected = GetCheckedContacts();
        if (selected.Length == 0)
        {
            MessageBox.Show("กรุณาเลือกรายชื่อก่อนเปลี่ยนสถานะ", "ยังไม่ได้เลือก", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = _databasePath;
        var repository = new ContactRepository(databasePath);
        var logs = new ActivityLogRepository(databasePath);
        foreach (var contact in selected)
        {
            var changed = consent
                ? await repository.SetConsentAsync(viewModel.ActiveAccount.Id, contact.Id, true)
                : await repository.SetOptOutAsync(viewModel.ActiveAccount.Id, contact.Id, true);
            if (changed)
            {
                await logs.AppendAsync(new ActivityLog(
                    viewModel.ActiveAccount.Id,
                    null,
                    "Info",
                    consent ? "ContactConsentGranted" : "ContactOptOutAdded",
                    $"Contact policy changed for telegram_user_id={contact.Id}.",
                    DateTimeOffset.UtcNow));
            }
        }
        await viewModel.LoadActivityLogsAsync(databasePath, viewModel.ActiveAccount.Id);
        await viewModel.LoadContactsAsync(databasePath, viewModel.ActiveAccount.Id);
    }

    private async void CreatePreview_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selected = viewModel.SelectedRows
            .Where(contact => contact.Selected && contact.ConsentState == ContactConsentState.Consented)
            .ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show("กรุณาติ๊กผู้รับที่มี Consent อย่างน้อย 1 รายการ", "ยังไม่มีผู้รับที่พร้อมส่ง", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(viewModel.MessageDraft))
        {
            MessageBox.Show("กรุณากรอกข้อความก่อนสร้าง Preview", "ยังไม่มีข้อความ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var linkNotice = string.IsNullOrWhiteSpace(viewModel.LinkDraft) ? "ไม่มีลิงก์" : "มีลิงก์แนบ";
        var preview = $"ผู้รับที่ผ่าน Consent: {selected.Length} รายการ\n\nข้อความ:\n{viewModel.MessageDraft}\n\n{linkNotice}\n\nงานจะถูกบันทึกเป็น WaitingForConfirmation และยังไม่ส่งจริง";
        if (MessageBox.Show(preview, "ตรวจสอบก่อนสร้าง Job", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
            return;

        var databasePath = _databasePath;
        var fingerprint = string.Join("|", selected.Select(contact => contact.Id).OrderBy(id => id));
        var definition = $"{viewModel.ActiveAccount.Id}|{fingerprint}|{viewModel.MessageDraft}|{viewModel.LinkDraft}";
        var idempotencyKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(definition)));
        var jobId = await new JobRepository(databasePath).CreateAsync(
            viewModel.ActiveAccount.Id, "ConsentMessaging", selected.Length, idempotencyKey,
            viewModel.MessageDraft.Trim(),
            string.IsNullOrWhiteSpace(viewModel.LinkDraft) ? null : viewModel.LinkDraft.Trim());
        var itemRepository = new JobItemRepository(databasePath);
        var existingItems = await itemRepository.ListAsync(jobId);
        if (existingItems.Count == 0)
        {
            var items = selected.Select(contact =>
            {
                var itemKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{jobId:D}|{viewModel.ActiveAccount.Id}|{contact.Id}")));
                return new JobItem(
                    jobId,
                    viewModel.ActiveAccount.Id,
                    contact.Id,
                    JobStatus.WaitingForConfirmation.ToString(),
                    0,
                    itemKey,
                    null,
                    DateTimeOffset.UtcNow);
            });
            await itemRepository.CreateAsync(items);
        }
        await new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(
            viewModel.ActiveAccount.Id,
            jobId,
            "Info",
            "JobCreatedWaitingForConfirmation",
            $"Created local preview job for {selected.Length} consented recipient(s).",
            DateTimeOffset.UtcNow));
        await viewModel.LoadQueueSummaryAsync(databasePath, viewModel.ActiveAccount.Id);
        await viewModel.LoadActivityLogsAsync(databasePath, viewModel.ActiveAccount.Id);
        MessageBox.Show($"สร้าง Job แล้ว\nสถานะ: WaitingForConfirmation\nJob ID: {jobId}\nยังไม่มีการส่งข้อความจริง", "Preview พร้อมแล้ว", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void ExportContacts_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV files (*.csv)|*.csv|Excel files (*.xlsx)|*.xlsx", FileName = "telegram-contacts.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var databasePath = _databasePath;
            var contacts = await new ContactRepository(databasePath).ListAsync(viewModel.ActiveAccount.Id);
            if (Path.GetExtension(dialog.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                await using var stream = File.Create(dialog.FileName);
                ContactXlsxExporter.Write(stream, contacts);
            }
            else
            {
                await File.WriteAllTextAsync(dialog.FileName, CsvExporter.ContactsForImport(contacts), Encoding.UTF8);
            }
            MessageBox.Show($"ส่งออก Contacts {contacts.Count} รายการแล้ว", "Export สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"ส่งออกไม่สำเร็จ\n{exception.Message}", "Export ไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TemplateEditor_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = _databasePath;
        new TemplateEditorWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void MediaLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = _databasePath;
        new MediaLibraryWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void CampaignWizard_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = _databasePath;
        new CampaignWizardWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void Groups_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = _databasePath;
        new GroupsWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void Queue_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = _databasePath;
        new QueueWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = _databasePath;
        new ActivityLogWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = _databasePath;
        new SettingsWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private async void ConnectAccount_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var account = viewModel.ActiveAccount;
        var databasePath = _databasePath;
        try
        {
            var configuration = await new TdLibConfigurationRepository(databasePath).GetAsync(account.Id);
            var native = await new TdLibConfigurationRepository(databasePath).GetNativeLocationAsync(account.Id);
            if (configuration is null || native is null)
            {
                MessageBox.Show("บัญชียังตั้งค่าไม่ครบ กรุณาเปิด ตั้งค่า แล้วบันทึก API ID, API Hash และเลือก tdjson.dll ก่อน", "ยังไม่พร้อมเข้าสู่ระบบ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var app = (App)Application.Current;
            if (app.TdLibClients.TryGet(account.Id, out _))
            {
                MessageBox.Show("บัญชีนี้เชื่อมต่ออยู่แล้ว หรือกำลังรอการยืนยันตัวตน", "กำลังเชื่อมต่อ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!app.TryHoldSessionLock(account.Id))
            {
                MessageBox.Show(
                    "เซสชัน Telegram ของบัญชีนี้ถูกโปรแกรมนี้เปิดอยู่แล้วในหน้าต่างหรืออินสแตนซ์อื่น\nปิดการเชื่อมต่อนั้นก่อน แล้วจึงเชื่อมต่อใหม่",
                    "เซสชันถูกใช้งานอยู่",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var repository = new AccountRepository(databasePath);
            var sessionDirectory = SessionPathPolicy.GetAccountSessionDirectory(account.Id);
            var bootstrapper = new TdLibSessionBootstrapper(app.TdLibClients, repository);
            await bootstrapper.ConnectAsync(account.Id, native.Value.Directory, native.Value.Sha256, configuration.Value.ApiId, configuration.Value.ApiHash, sessionDirectory);

            if (bootstrapper.TryGetStateMachine(account.Id, out var machine) && machine is not null)
            {
                var coordinator = new TdLibAccountAuthorizationCoordinator(new TdLibAccountSessionCoordinator(app.TdLibClients), repository);
                new AuthenticationWizardWindow(account.Id, machine, coordinator) { Owner = this }.ShowDialog();
                if (machine.IsReady && app.TdLibClients.TryGet(account.Id, out var connectedClient) && connectedClient is not null)
                {
                    await new TdLibAccountProfileSyncService(connectedClient, repository).SyncAsync(account.Id);
                    await viewModel.LoadAccountsAsync(databasePath);
                    viewModel.ActiveAccount = viewModel.Accounts.FirstOrDefault(item => item.Id == account.Id);
                    MessageBox.Show("ยืนยันตัวตนสำเร็จแล้ว สามารถ Sync Contacts ได้", "เข้าสู่ระบบสำเร็จ", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        catch (Exception exception)
        {
            ((App)Application.Current).ReleaseSessionLock(account.Id);
            MessageBox.Show($"เริ่มเข้าสู่ระบบไม่สำเร็จ\n{exception.Message}", "เชื่อมต่อไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ExportDashboardReport_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"telegram-dashboard-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var databasePath = _databasePath;
            DateTimeOffset? from = StartDateBox.SelectedDate?.Date;
            DateTimeOffset? to = EndDateBox.SelectedDate?.Date.AddDays(1).AddTicks(-1);
            if (from.HasValue && to.HasValue && from > to)
            {
                MessageBox.Show("วันที่เริ่มต้องไม่หลังวันที่สิ้นสุด", "ช่วงเวลาไม่ถูกต้อง", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var metrics = await new DashboardMetricsRepository(databasePath).LoadAsync(viewModel.ActiveAccount.Id, from, to);
            var rows = new[]
            {
                "metric,value",
                $"account_id,{viewModel.ActiveAccount.Id}",
                $"generated_at_utc,{DateTimeOffset.UtcNow:O}",
                $"from_utc,{from?.ToString("O") ?? ""}",
                $"to_utc,{to?.ToString("O") ?? ""}",
                $"total_jobs,{metrics.TotalJobs}",
                $"completed_jobs,{metrics.CompletedJobs}",
                $"failed_jobs,{metrics.FailedJobs}",
                $"queued_jobs,{metrics.QueuedJobs}",
                $"flood_wait_jobs,{metrics.FloodWaitJobs}",
                $"campaign_jobs,{metrics.CampaignJobs}",
                $"campaign_completed_jobs,{metrics.CampaignCompletedJobs}",
                $"campaign_failed_jobs,{metrics.CampaignFailedJobs}",
                $"permission_errors,{metrics.PermissionErrors}",
                $"privacy_errors,{metrics.PrivacyErrors}",
                $"network_errors,{metrics.NetworkErrors}"
            };
            await File.WriteAllLinesAsync(dialog.FileName, rows, Encoding.UTF8);
            MessageBox.Show("ส่งออก Dashboard Report สำเร็จ", "Export สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"ส่งออกไม่สำเร็จ\n{exception.Message}", "Export ไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

