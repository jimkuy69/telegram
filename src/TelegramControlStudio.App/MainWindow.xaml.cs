using System.Windows;
using System.IO;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.App.ViewModels;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Core.Export;
using TelegramControlStudio.Infrastructure.Telegram;
using TelegramControlStudio.Core.Security;
using System.Text;

namespace TelegramControlStudio.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainWindowViewModel();
        DataContext = viewModel;
        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TelegramControlStudio",
            "telegram-control-studio.db");
        _ = LoadAccountsAsync(viewModel, databasePath);
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
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
        new PendingRequestsWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void InviteLinks_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
        new InviteLinksWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void ImportContacts_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
        new ContactImportWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private async void SearchContacts_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null) return;
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
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
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
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
        if (!app.TdLibClients.TryGet(viewModel.ActiveAccount.Id, out var client) || client is null)
        {
            MessageBox.Show("บัญชีนี้ยังไม่ได้เชื่อมต่อและยืนยันตัวตนกับ TDLib", "ยังไม่พร้อม Sync", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
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

    private async void DeleteSelectedContacts_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null) return;
        var selected = ContactGrid.SelectedItems.OfType<ContactRow>().ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show("กรุณาเลือกรายชื่อที่ต้องการลบ", "ยังไม่ได้เลือก", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var answer = MessageBox.Show($"ยืนยัน Soft Delete จำนวน {selected.Length} รายการ? ข้อมูลจะไม่ถูกลบถาวร", "ยืนยันการลบ", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
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

    private async void SetConsent_Click(object sender, RoutedEventArgs e) => await UpdateSelectedPolicyAsync(true);

    private async void SetOptOut_Click(object sender, RoutedEventArgs e) => await UpdateSelectedPolicyAsync(false);

    private async Task UpdateSelectedPolicyAsync(bool consent)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null) return;
        var selected = ContactGrid.SelectedItems.OfType<ContactRow>().ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show("กรุณาเลือกรายชื่อก่อนเปลี่ยนสถานะ", "ยังไม่ได้เลือก", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
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
            var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
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
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
        new TemplateEditorWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void MediaLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
        new MediaLibraryWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void CampaignWizard_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
        new CampaignWizardWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void Groups_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
        new GroupsWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void Queue_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
        new QueueWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
        new ActivityLogWindow(databasePath, viewModel.ActiveAccount.Id) { Owner = this }.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null)
        {
            MessageBox.Show("กรุณาเพิ่มหรือเลือกบัญชีก่อน", "ยังไม่มีบัญชี", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
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
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
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
            var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
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

