using System.Windows;
using System.IO;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.App.ViewModels;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Core.Export;
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

    private void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TelegramControlStudio",
            "telegram-control-studio.db");
        var wizard = new AddAccountWindow(databasePath) { Owner = this };
        if (wizard.ShowDialog() == true)
            Title = "Telegram Control Studio • บันทึกบัญชีแล้ว";
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
            MessageBox.Show($"ค้นหาไม่สำเร็จ
{exception.Message}", "ค้นหาไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RefreshContacts_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveAccount is null) return;
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "telegram-control-studio.db");
        await viewModel.LoadContactsAsync(databasePath, viewModel.ActiveAccount.Id);
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
            MessageBox.Show($"ส่งออกไม่สำเร็จ
{exception.Message}", "Export ไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Warning);
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
}