using System.Windows;
using System.IO;
using Microsoft.Win32;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Security;
using TelegramControlStudio.Infrastructure.Telegram;
using TelegramControlStudio.Core.Security;
namespace TelegramControlStudio.App;
public partial class SettingsWindow : Window
{
    private readonly string _databasePath; private readonly long _accountId;
    private string? _nativeDirectory;
    public SettingsWindow(string databasePath, long accountId) { InitializeComponent(); _databasePath = databasePath; _accountId = accountId; DatabasePathBox.Text = databasePath; }
    private async void SaveTdLib_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ApiIdBox.Text.Trim(), out var apiId) || apiId <= 0 || string.IsNullOrWhiteSpace(ApiHashBox.Password)) { StatusText.Text = "กรุณากรอก API ID และ API Hash ให้ถูกต้อง"; return; }
        try { await new TdLibConfigurationRepository(_databasePath).SaveAsync(_accountId, apiId, ApiHashBox.Password); ApiHashBox.Clear(); StatusText.Text = "บันทึก TDLib Configuration แบบเข้ารหัสแล้ว"; } catch (Exception exception) { ApiHashBox.Clear(); StatusText.Text = $"บันทึกไม่สำเร็จ: {exception.Message}"; }
    }
    private void SelectNative_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "TDLib native library (tdjson.dll)|tdjson.dll" }; if (dialog.ShowDialog() != true) return;
        _nativeDirectory = Path.GetDirectoryName(dialog.FileName);
        NativePathText.Text = $"Native TDLib: {_nativeDirectory}\nตรวจ hash ก่อนเชื่อมต่อทุกครั้ง";
    }
    private async void ConnectNative_Click(object sender, RoutedEventArgs e)
    {
        if (_nativeDirectory is null || !int.TryParse(ApiIdBox.Text.Trim(), out var apiId) || apiId <= 0 || string.IsNullOrWhiteSpace(ApiHashBox.Password)) { StatusText.Text = "กรุณากรอก API ID, API Hash และเลือก tdjson.dll ก่อนเชื่อมต่อ"; return; }
        try
        {
            var loader = new TdLibNativeLoader();
            var hash = loader.ComputeSha256(_nativeDirectory);
            var app = (App)Application.Current;
            var repository = new AccountRepository(_databasePath);
            var sessionDirectory = SessionPathPolicy.GetAccountSessionDirectory(_accountId);
            var bootstrapper = new TdLibSessionBootstrapper(app.TdLibClients, repository);
            await bootstrapper.ConnectAsync(_accountId, _nativeDirectory, hash, apiId, ApiHashBox.Password, sessionDirectory);
            ApiHashBox.Clear();
            StatusText.Text = $"Native TDLib เชื่อมต่อแล้ว\nSHA-256: {hash}";
            if (bootstrapper.TryGetStateMachine(_accountId, out var machine) && machine is not null)
            {
                var coordinator = new TdLibAccountAuthorizationCoordinator(new TdLibAccountSessionCoordinator(app.TdLibClients), repository);
                new AuthenticationWizardWindow(_accountId, machine, coordinator) { Owner = this }.ShowDialog();
            }
        }
        catch (Exception exception) { ApiHashBox.Clear(); StatusText.Text = $"เชื่อมต่อไม่สำเร็จ: {exception.Message}"; }
    }
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Encrypted Telegram Control Studio backup (*.tcsbackup)|*.tcsbackup", FileName = $"telegram-control-studio-{DateTime.Now:yyyyMMdd-HHmmss}.tcsbackup" }; if (dialog.ShowDialog() != true) return;
        try { await new EncryptedBackupService().BackupAsync(_databasePath, dialog.FileName); await LogAsync("BackupCreated", $"Encrypted backup created: {Path.GetFileName(dialog.FileName)}"); StatusText.Text = "สร้าง Backup สำเร็จ"; } catch (Exception exception) { StatusText.Text = $"Backup ไม่สำเร็จ: {exception.Message}"; }
    }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Encrypted Telegram Control Studio backup (*.tcsbackup)|*.tcsbackup" }; if (dialog.ShowDialog() != true) return;
        if (MessageBox.Show("Restore จะเขียนทับ Database ปัจจุบัน ยืนยันหรือไม่?", "ยืนยันการ Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await new EncryptedBackupService().RestoreAsync(dialog.FileName, _databasePath); await LogAsync("BackupRestored", $"Encrypted backup restored: {Path.GetFileName(dialog.FileName)}"); StatusText.Text = "Restore สำเร็จ กรุณาเปิดโปรแกรมใหม่เพื่อโหลดข้อมูล"; } catch (Exception exception) { StatusText.Text = $"Restore ไม่สำเร็จ: {exception.Message}"; }
    }
    private Task LogAsync(string eventType, string message) => new ActivityLogRepository(_databasePath).AppendAsync(new TelegramControlStudio.Core.Domain.ActivityLog(_accountId, null, "Info", eventType, message, DateTimeOffset.UtcNow));
}

