using System.Windows;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App;

public partial class CampaignWizardWindow : Window
{
    private readonly string _databasePath;
    private readonly long _accountId;
    private IReadOnlyList<Contact> _contacts = [];
    public CampaignWizardWindow(string databasePath, long accountId)
    {
        InitializeComponent(); _databasePath = databasePath; _accountId = accountId;
        Loaded += async (_, _) => { _contacts = await new ContactRepository(_databasePath).ListAsync(_accountId); RecipientSummary.Text = $"ผู้รับที่ผ่าน Consent: {_contacts.Count(item => item.HasConsent && !item.IsOptedOut)} รายการ"; };
    }
    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(TemplateIdBox.Text, out var templateId) || string.IsNullOrWhiteSpace(NameBox.Text)) { MessageBox.Show("กรุณาระบุชื่อและ Template ID", "ข้อมูลไม่ครบ"); return; }
        if (MessageBox.Show("ยืนยันการสร้าง Campaign หรือไม่? ระบบยังไม่ส่งข้อความจริง", "ยืนยัน", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var id = await new CampaignBuilder(_databasePath).CreateFromConsentedContactsAsync(_accountId, templateId, NameBox.Text.Trim(), PolicyBox.Text.Trim(), _contacts, true);
            var jobId = await new CampaignQueueService(_databasePath).QueueCampaignAsync(id, _accountId, true);
            MessageBox.Show($"สร้าง Campaign และเข้าคิว Job แล้ว\nCampaign: {id}\nJob: {jobId}\n\nระบบยังไม่ส่งข้อความจริง", "สำเร็จ");
        }
        catch (Exception exception)
        {
            MessageBox.Show($"สร้าง Campaign ไม่สำเร็จ\n{exception.Message}", "ตรวจสอบข้อมูล", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

