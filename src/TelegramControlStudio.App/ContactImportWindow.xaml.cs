using System.IO;
using System.Windows;
using Microsoft.Win32;
using TelegramControlStudio.Core.Import;
using TelegramControlStudio.Infrastructure.Import;

namespace TelegramControlStudio.App;

public partial class ContactImportWindow : Window
{
    private readonly string _databasePath;
    private readonly long _accountId;
    private ContactImportResult? _preview;
    public ContactImportWindow(string databasePath, long accountId)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _accountId = accountId;
    }

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        _preview = ContactCsvImporter.Preview(File.ReadAllText(dialog.FileName), _accountId);
        SummaryText.Text = $"ผ่าน {_preview.Valid.Count} รายการ • ผิดพลาด {_preview.Errors.Count} รายการ";
        PreviewText.Text = _preview.Errors.Count == 0 ? "พร้อม Import ทุกแถวที่ผ่านการตรวจ" : string.Join(Environment.NewLine, _preview.Errors);
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_preview is null) { MessageBox.Show("กรุณาเลือก CSV และ Preview ก่อน", "ยังไม่มี Preview"); return; }
        if (MessageBox.Show($"ยืนยัน Import {_preview.Valid.Count} รายการหรือไม่?", "ยืนยัน", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var result = await new ContactImportService(_databasePath).CommitAsync(_preview, true);
        MessageBox.Show($"นำเข้าสำเร็จ {result.Imported} รายการ\nข้าม {result.Skipped} รายการ", "เสร็จสิ้น");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

