using System.Collections.ObjectModel;

namespace TelegramControlStudio.App.ViewModels;

public sealed class MainWindowViewModel
{
    public ObservableCollection<ContactRow> Contacts { get; } =
    [
        new(true, "ตัวอย่างผู้รับ A", 100000001, "@example_a", "ได้รับความยินยอม"),
        new(false, "ตัวอย่างผู้รับ B", 100000002, "@example_b", "ยังไม่อนุญาต"),
        new(true, "ตัวอย่างผู้รับ C", 100000003, "@example_c", "ได้รับความยินยอม")
    ];

    public string DataSourceNotice { get; private set; } = "ข้อมูลตัวอย่างในเครื่องเท่านั้น • ยังไม่ได้ Sync Telegram";

    public int ConsentCount => Contacts.Count(contact => contact.Selected && contact.Consent == "ได้รับความยินยอม");

    public bool CanCreateJob => ConsentCount > 0;

    public RelayCommand CreateJobCommand { get; }

    public MainWindowViewModel()
    {
        CreateJobCommand = new RelayCommand(CreateJob, () => CanCreateJob);
    }

    private void CreateJob()
    {
        // Intentionally remains a local preview boundary until TDLib and confirmation gates exist.
        DataSourceNotice = $"พร้อมสร้าง Job แบบ Preview สำหรับ {ConsentCount} ผู้รับที่ยินยอม";
    }
}
