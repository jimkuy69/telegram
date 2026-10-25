using System.Windows;
using System.IO;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Security;

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

        string phoneNumber;
        try
        {
            phoneNumber = TelegramPhoneNumberValidator.Normalize(PhoneNumberBox.Text);
        }
        catch (FormatException exception)
        {
            MessageBox.Show(exception.Message, "รูปแบบเบอร์โทรศัพท์ไม่ถูกต้อง", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var repository = new AccountRepository(_databasePath);
        var accountId = await repository.CreateAsync(DisplayNameBox.Text.Trim(), phoneNumber);
        var sessionDirectory = SessionPathPolicy.GetAccountSessionDirectory(accountId);
        Directory.CreateDirectory(sessionDirectory);
        await repository.SaveSessionAsync(new AccountSession(accountId, sessionDirectory, "PendingAuthorization", false, DateTimeOffset.UtcNow));
        MessageBox.Show("บันทึกบัญชีและโฟลเดอร์ Session ในเครื่องแล้ว ขั้นตอน Login จะเปิดใช้หลัง TDLib ผ่านทั้ง Hash Gate และ Native Runtime Gate", "สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}

