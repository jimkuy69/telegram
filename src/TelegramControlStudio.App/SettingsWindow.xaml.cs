using System.Windows;
using System.IO;
using Microsoft.Win32;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Security;
namespace TelegramControlStudio.App;
public partial class SettingsWindow : Window
{
    private readonly string _databasePath; private readonly long _accountId;
    public SettingsWindow(string databasePath, long accountId) { InitializeComponent(); _databasePath = databasePath; _accountId = accountId; DatabasePathBox.Text = databasePath; }
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

