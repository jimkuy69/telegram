using System.Windows;
using System.IO;
using Microsoft.Win32;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Security;
using TelegramControlStudio.Infrastructure.Telegram;
using TelegramControlStudio.Core.Security;
namespace TelegramControlStudio.App;
public partial class SettingsWindow : Window
{
    private readonly string _databasePath; private readonly long _accountId;
    private string? _nativeDirectory;
    private readonly Task _tdLibLoadTask;
    public SettingsWindow(string databasePath, long accountId) { InitializeComponent(); _databasePath = databasePath; _accountId = accountId; DatabasePathBox.Text = databasePath; _tdLibLoadTask = LoadTdLibIdAsync(); _ = LoadSafetyAsync(); }
    private async Task LoadSafetyAsync()
    {
        try
        {
            var settings = await new SendSafetyRepository(_databasePath).GetAsync();
            TestModeBox.IsChecked = settings.TestModeEnabled;
            AllowlistBox.Text = SendSafetyPolicy.FormatAllowlist(settings.AllowlistUserIds);
            ClearEmergencyButton.IsEnabled = settings.KillSwitchEngaged;
            SafetyStatusText.Text = SendSafetyPolicy.Describe(settings, settings.AllowlistUserIds.Count);
            SafetyStatusText.Foreground = (System.Windows.Media.Brush)FindResource(settings.KillSwitchEngaged ? "DangerText" : settings.TestModeEnabled ? "WarningText" : "SuccessText");
        }
        catch (Exception exception) { SafetyStatusText.Text = $"อ่านการตั้งค่าความปลอดภัยไม่สำเร็จ: {exception.Message}"; }
    }
    private async void SaveSafety_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var repository = new SendSafetyRepository(_databasePath);
            var current = await repository.GetAsync();
            var testMode = TestModeBox.IsChecked == true;
            if (current.TestModeEnabled && !testMode &&
                MessageBox.Show("การปิดโหมดทดสอบจะอนุญาตให้ส่งได้กับทุกคนที่ให้ Consent ไม่จำกัดเฉพาะ allowlist ยืนยันหรือไม่?", "ปิดโหมดทดสอบ", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                TestModeBox.IsChecked = true;
                return;
            }
            var updated = current with
            {
                TestModeEnabled = testMode,
                AllowlistUserIds = SendSafetyPolicy.ParseAllowlist(AllowlistBox.Text)
            };
            await repository.SaveAsync(updated);
            await LogAsync("SendSafetyUpdated", $"TestMode={updated.TestModeEnabled}, AllowlistCount={updated.AllowlistUserIds.Count}");
            SafetyStatusText.Text = $"บันทึกแล้ว • {SendSafetyPolicy.Describe(updated, updated.AllowlistUserIds.Count)}";
            SafetyStatusText.Foreground = (System.Windows.Media.Brush)FindResource(updated.TestModeEnabled ? "WarningText" : "SuccessText");
        }
        catch (Exception exception) { SafetyStatusText.Text = $"บันทึกความปลอดภัยไม่สำเร็จ: {exception.Message}"; }
    }
    private async void ClearEmergency_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("ปลดหยุดฉุกเฉิน? ระบบจะกลับมาส่งได้ตามโหมดทดสอบหรือโหมดใช้งานจริงที่ตั้งไว้", "ปลดหยุดฉุกเฉิน", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await new SendSafetyRepository(_databasePath).SetKillSwitchAsync(false);
            await LogAsync("KillSwitchCleared", "Kill switch cleared from Settings.");
            await LoadSafetyAsync();
            SafetyStatusText.Text = "ปลดหยุดฉุกเฉินแล้ว";
        }
        catch (Exception exception) { SafetyStatusText.Text = $"ปลดหยุดฉุกเฉินไม่สำเร็จ: {exception.Message}"; }
    }
    private async Task LoadTdLibIdAsync()
    {
        try
        {
            var configuration = await new TdLibConfigurationRepository(_databasePath).GetAsync(_accountId);
            if (configuration is not null) ApiIdBox.Text = configuration.Value.ApiId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var native = await new TdLibConfigurationRepository(_databasePath).GetNativeLocationAsync(_accountId);
            if (native is not null && Directory.Exists(native.Value.Directory)) { _nativeDirectory = native.Value.Directory; NativePathText.Text = $"Native TDLib: {_nativeDirectory}\nบันทึก hash: {native.Value.Sha256}"; }
        }
        catch { /* Settings remains usable when the local database is unavailable. */ }
    }
    private async void SaveTdLib_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ApiIdBox.Text.Trim(), out var apiId) || apiId <= 0 || string.IsNullOrWhiteSpace(ApiHashBox.Password)) { StatusText.Text = "กรุณากรอก API ID และ API Hash ให้ถูกต้อง"; return; }
        try { await new TdLibConfigurationRepository(_databasePath).SaveAsync(_accountId, apiId, ApiHashBox.Password); ApiHashBox.Clear(); StatusText.Text = "บันทึก TDLib Configuration แบบเข้ารหัสแล้ว"; } catch (Exception exception) { ApiHashBox.Clear(); StatusText.Text = $"บันทึกไม่สำเร็จ: {exception.Message}"; }
    }
    private async void SelectNative_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "TDLib native library (tdjson.dll)|tdjson.dll" }; if (dialog.ShowDialog() != true) return;
        _nativeDirectory = Path.GetDirectoryName(dialog.FileName);
        NativePathText.Text = $"Native TDLib: {_nativeDirectory}\nตรวจ hash ก่อนเชื่อมต่อทุกครั้ง";
        try
        {
            var hash = new TdLibNativeLoader().ComputeSha256(_nativeDirectory!);
            await new TdLibConfigurationRepository(_databasePath).SaveNativeLocationAsync(_accountId, _nativeDirectory!, hash);
            NativePathText.Text = $"Native TDLib: {_nativeDirectory}\nบันทึก hash: {hash}";
        }
        catch (Exception exception) { StatusText.Text = $"ตรวจ Native TDLib ไม่สำเร็จ: {exception.Message}"; }
    }
    private async void ConnectNative_Click(object sender, RoutedEventArgs e)
    {
        await _tdLibLoadTask;
        var configuration = await new TdLibConfigurationRepository(_databasePath).GetAsync(_accountId);
        if (_nativeDirectory is null || configuration is null) { StatusText.Text = "กรุณาบันทึก API ID, API Hash และเลือก tdjson.dll ก่อนเชื่อมต่อ"; return; }
        try
        {
            var app = (App)Application.Current;
            if (!app.TryHoldSessionLock(_accountId))
            {
                StatusText.Text = "เซสชัน Telegram ของบัญชีนี้ถูกเปิดอยู่แล้วในหน้าต่างหรืออินสแตนซ์อื่นของโปรแกรม — ปิดการเชื่อมต่อนั้นก่อน";
                return;
            }

            var loader = new TdLibNativeLoader();
            var hash = loader.ComputeSha256(_nativeDirectory);
            var repository = new AccountRepository(_databasePath);
            var sessionDirectory = SessionPathPolicy.GetAccountSessionDirectory(_accountId);
            var bootstrapper = new TdLibSessionBootstrapper(app.TdLibClients, repository, app.ConsentReplySinkFactory);
            await bootstrapper.ConnectAsync(_accountId, _nativeDirectory, hash, configuration.Value.ApiId, configuration.Value.ApiHash, sessionDirectory);
            ApiHashBox.Clear();
            StatusText.Text = $"Native TDLib เชื่อมต่อแล้ว\nSHA-256: {hash}";
            if (bootstrapper.TryGetStateMachine(_accountId, out var machine) && machine is not null)
            {
                var coordinator = new TdLibAccountAuthorizationCoordinator(new TdLibAccountSessionCoordinator(app.TdLibClients), repository);
                new AuthenticationWizardWindow(_accountId, machine, coordinator) { Owner = this }.ShowDialog();
                if (machine.IsReady && app.TdLibClients.TryGet(_accountId, out var connectedClient) && connectedClient is not null)
                {
                    await new TdLibAccountProfileSyncService(connectedClient, repository).SyncAsync(_accountId);
                    StatusText.Text = "ยืนยันตัวตนแล้ว และ Sync โปรไฟล์บัญชีจาก Telegram สำเร็จ";
                }
            }
        }
        catch (Exception exception) { ((App)Application.Current).ReleaseSessionLock(_accountId); ApiHashBox.Clear(); StatusText.Text = $"เชื่อมต่อไม่สำเร็จ: {exception.Message}"; }
    }
    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("ยืนยันออกจากระบบบัญชีนี้? Session ของบัญชีนี้จะถูกถอดออกจากโปรแกรม", "ยืนยัน Logout", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var app = (App)Application.Current;
            await new TdLibAccountLogoutService(app.TdLibClients, new AccountRepository(_databasePath)).LogoutAsync(_accountId);
            StatusText.Text = "Logout สำเร็จ บัญชีนี้อยู่สถานะ Offline";
        }
        catch (Exception exception) { StatusText.Text = $"Logout ไม่สำเร็จ: {exception.Message}"; }
    }
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Encrypted Telegram Control Studio backup (*.tcsbackup)|*.tcsbackup", FileName = $"telegram-control-studio-{DateTime.Now:yyyyMMdd-HHmmss}.tcsbackup" }; if (dialog.ShowDialog() != true) return;
        try { await new EncryptedBackupService().BackupAsync(_databasePath, dialog.FileName); await LogAsync("BackupCreated", $"Encrypted backup created: {Path.GetFileName(dialog.FileName)}"); StatusText.Text = "สร้าง Backup สำเร็จ"; } catch (Exception exception) { StatusText.Text = $"Backup ไม่สำเร็จ: {exception.Message}"; }
    }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        if (app.TdLibClients.Count > 0) { StatusText.Text = "กรุณา Logout/ปิด TDLib ทุกบัญชีก่อน Restore เพื่อป้องกันฐานข้อมูลไม่ตรงกับ Session"; return; }
        var dialog = new OpenFileDialog { Filter = "Encrypted Telegram Control Studio backup (*.tcsbackup)|*.tcsbackup" }; if (dialog.ShowDialog() != true) return;
        if (MessageBox.Show("Restore จะเขียนทับ Database ปัจจุบัน ยืนยันหรือไม่?", "ยืนยันการ Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await new EncryptedBackupService().RestoreAsync(dialog.FileName, _databasePath); await LogAsync("BackupRestored", $"Encrypted backup restored: {Path.GetFileName(dialog.FileName)}"); StatusText.Text = "Restore สำเร็จ กรุณาเปิดโปรแกรมใหม่เพื่อโหลดข้อมูล"; } catch (Exception exception) { StatusText.Text = $"Restore ไม่สำเร็จ: {exception.Message}"; }
    }
    private Task LogAsync(string eventType, string message) => new ActivityLogRepository(_databasePath).AppendAsync(new TelegramControlStudio.Core.Domain.ActivityLog(_accountId, null, "Info", eventType, message, DateTimeOffset.UtcNow));
}
