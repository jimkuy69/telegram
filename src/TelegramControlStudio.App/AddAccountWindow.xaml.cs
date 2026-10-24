using System.Windows;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App;

public partial class AddAccountWindow : Window
{
    private readonly string _databasePath;

    public AddAccountWindow(string databasePath)
    {
        InitializeComponent();
        _databasePath = databasePath;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text) ||
            string.IsNullOrWhiteSpace(PhoneNumberBox.Text))
        {
            MessageBox.Show("กรุณากรอกชื่อและเบอร์โทรศัพท์", "ข้อมูลไม่ครบ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var repository = new AccountRepository(_databasePath);
        await repository.CreateAsync(DisplayNameBox.Text.Trim(), PhoneNumberBox.Text.Trim());
        MessageBox.Show("บันทึกบัญชีในเครื่องแล้ว ขั้นตอน Login จะเปิดใช้หลัง TDLib ผ่านการตรวจสอบ", "สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
