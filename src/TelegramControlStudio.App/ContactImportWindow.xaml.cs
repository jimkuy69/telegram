using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using TelegramControlStudio.Core.Import;
using TelegramControlStudio.Infrastructure.Import;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.App;

public partial class ContactImportWindow : Window
{
    private readonly string _databasePath;
    private long _accountId;
    private ContactImportResult? _preview;
    public ContactImportWindow(string databasePath, long accountId)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _accountId = accountId;
        AccountSelector.Initialize(databasePath, accountId);
        AccountSelector.AccountChanged += OnAccountChanged;
    }

    private void OnAccountChanged(object? sender, long accountId)
    {
        _accountId = accountId;
        _preview = null;
        SummaryText.Text = "เลือกไฟล์เพื่อเริ่ม Preview";
        PreviewText.Text = string.Empty;
    }

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Contact files (*.csv;*.xlsx)|*.csv;*.xlsx|CSV files (*.csv)|*.csv|Excel files (*.xlsx)|*.xlsx", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        if (Path.GetExtension(dialog.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(dialog.FileName);
            _preview = ContactXlsxImporter.Preview(stream, _accountId);
        }
        else
        {
            _preview = ContactCsvImporter.Preview(File.ReadAllText(dialog.FileName), _accountId);
        }
        SummaryText.Text = $"ผ่าน {_preview.Valid.Count} รายการ • รอ resolve จากเบอร์โทร {_preview.PhoneOnly.Count} รายการ • ผิดพลาด {_preview.Errors.Count} รายการ";
        PreviewText.Text = _preview.Errors.Count == 0 ? "พร้อม Import ทุกแถวที่ผ่านการตรวจ" : string.Join(Environment.NewLine, _preview.Errors);
    }

    private void DownloadTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = "telegram-contacts-template.csv",
            Filter = "CSV files (*.csv)|*.csv",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;

        const string template =
            "telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out\r\n" +
            "123456789,ชื่อ,นามสกุล,example,+66900000000,false,false\r\n";
        File.WriteAllText(dialog.FileName, template, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        MessageBox.Show("บันทึก Template แล้ว\nกรอกให้ครบ 7 คอลัมน์ ถ้าไม่รู้ telegram_user_id ให้เว้นคอลัมน์นั้นแล้วใส่ phone_number รูปแบบสากล (เช่น +66812345678) แอปจะหา ID ให้ผ่าน Telegram\nห้ามเว้น has_consent / is_opted_out (ใช้ true/false หรือ 1/0)", "สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_preview is null) { MessageBox.Show("กรุณาเลือก CSV/XLSX และ Preview ก่อน", "ยังไม่มี Preview"); return; }
        if (_preview.Valid.Count == 0 && _preview.PhoneOnly.Count == 0)
        {
            MessageBox.Show("ไม่มีแถวที่พร้อม Import ตรวจคอลัมน์ telegram_user_id หรือ phone_number อีกครั้ง", "ไม่มีแถวที่ผ่าน", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show($"ยืนยัน Import {_preview.Valid.Count} รายการที่มี telegram_user_id อยู่แล้วหรือไม่?", "ยืนยัน", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        // importContacts writes to the Telegram account itself, so a double click must not send the
        // same numbers twice.
        ((System.Windows.Controls.Button)sender).IsEnabled = false;
        try
        {
            var result = await new ContactImportService(_databasePath).CommitAsync(_preview, true);
            var report = new List<string> { $"นำเข้าจาก telegram_user_id {result.Imported} รายการ" };
            if (_preview.Errors.Count > 0) report.Add($"ข้าม {result.Errors.Count} แถวที่ตรวจไม่ผ่าน");
            report.AddRange(await ResolveByPhoneAsync());
            PreviewText.Text = string.Join(Environment.NewLine, report);
            SummaryText.Text = string.Join(" • ", report);
        }
        finally
        {
            ((System.Windows.Controls.Button)sender).IsEnabled = true;
        }
    }

    // A list of phone numbers is the normal shape of a marketing list, and only Telegram can say which
    // number belongs to which account, so these rows are resolved through importContacts.
    private async Task<IReadOnlyList<string>> ResolveByPhoneAsync()
    {
        var phoneOnly = _preview!.PhoneOnly;
        if (phoneOnly.Count == 0) return [];
        var app = (App)Application.Current;
        if (!app.TdLibClients.TryGet(_accountId, out var client) || client is null)
            return [$"ยังไม่ resolve {phoneOnly.Count} เบอร์ เพราะบัญชียังไม่เชื่อมต่อและยืนยันตัวตนกับ TDLib"];
        if (MessageBox.Show(
                $"ต้องส่งเบอร์ {phoneOnly.Count} รายการให้ Telegram เพื่อหา Telegram ID\n\n" +
                "Telegram จะเพิ่มเบอร์เหล่านี้เป็น \"รายชื่อติดต่อ\" ของบัญชีนี้ และจะเห็นทุกอุปกรณ์ของบัญชี\n" +
                "การ resolve ไม่ได้ส่งข้อความหาใคร และไม่ได้ให้ consent\n\nยืนยัน?",
                "ยืนยันครั้งที่ 2", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return [$"ข้าม {phoneOnly.Count} เบอร์ เพราะไม่ได้ยืนยัน importContacts"];

        var import = await new TdLibContactPhoneImportService(_databasePath).ImportAsync(
            client, _accountId, phoneOnly, true,
            new Progress<(int Done, int Total)>(progress => SummaryText.Text = $"resolve เบอร์ {progress.Done}/{progress.Total} ..."));
        var lines = new List<string> { $"resolve จากเบอร์โทรสำเร็จ {import.Resolved.Count} รายการ" };
        if (import.Unresolved.Count > 0) lines.Add($"ไม่ได้อันดับ id {import.Unresolved.Count} เบอร์");
        lines.AddRange(import.Errors);
        lines.AddRange(import.Unresolved);
        return lines;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

