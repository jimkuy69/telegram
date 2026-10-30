using System.Windows;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;

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
            var template = await ResolveTemplateAsync(templateId);
            if (template is null) { MessageBox.Show("ไม่พบ Template นี้ในบัญชีของคุณ", "Template ไม่ถูกต้อง", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var id = await new CampaignBuilder(_databasePath).CreateFromConsentedContactsAsync(_accountId, templateId, NameBox.Text.Trim(), PolicyBox.Text.Trim(), _contacts, true);
            var jobId = await new CampaignQueueService(_databasePath).QueueCampaignAsync(id, _accountId, template.Body, template.Link, true);
            MessageBox.Show($"สร้าง Campaign และเข้าคิว Job แล้ว\nCampaign: {id}\nJob: {jobId}\n\nระบบยังไม่ส่งข้อความจริง", "สำเร็จ");
        }
        catch (Exception exception)
        {
            MessageBox.Show($"สร้าง Campaign ไม่สำเร็จ\n{exception.Message}", "ตรวจสอบข้อมูล", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private async Task<MessageTemplate?> ResolveTemplateAsync(long templateId) =>
        (await new TemplateRepository(_databasePath).ListAsync(_accountId, activeOnly: false)).FirstOrDefault(template => template.Id == templateId);

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(TemplateIdBox.Text, out var templateId) || string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(MessageTextBox.Text))
        { MessageBox.Show("กรุณาระบุชื่อ Template ID และข้อความ", "ข้อมูลไม่ครบ"); return; }
        var app = (App)Application.Current;
        if (!app.TdLibClients.TryGet(_accountId, out var client) || client is null)
        { MessageBox.Show("บัญชีนี้ยังไม่ได้เชื่อมต่อและยืนยันตัวตนกับ TDLib", "ยังไม่พร้อมส่ง"); return; }
        var eligible = _contacts.Count(item => item.HasConsent && !item.IsOptedOut);
        if (eligible == 0) { MessageBox.Show("ไม่มีผู้รับที่ผ่าน Consent", "ส่งไม่ได้"); return; }
        if (MessageBox.Show($"ยืนยันส่งข้อความจริงให้ผู้รับที่มี Consent จำนวน {eligible} รายการ?\nระบบจะส่งทีละรายการและข้าม Opt-out", "ยืนยันการส่งจริง", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var text = MessageTextBox.Text.Trim();
            var id = await new CampaignBuilder(_databasePath).CreateFromConsentedContactsAsync(_accountId, templateId, NameBox.Text.Trim(), PolicyBox.Text.Trim(), _contacts, true);
            var jobId = await new CampaignQueueService(_databasePath).QueueCampaignAsync(id, _accountId, text, null, true);
            if (!app.JobGate.TryBegin(jobId)) { MessageBox.Show("งานนี้กำลังถูกรันอยู่", "รันซ้ำไม่ได้", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            JobStatus status;
            try
            {
                status = await new TdLibCampaignJobRunner(_databasePath).RunAsync(client, jobId, _accountId, true, TimeSpan.FromSeconds(1));
            }
            finally
            {
                app.JobGate.End(jobId);
            }
            MessageBox.Show($"Campaign ส่งเสร็จตามสถานะ Job: {status}\nJob: {jobId}", "ดำเนินการเสร็จ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) { MessageBox.Show($"ส่ง Campaign ไม่สำเร็จ\n{exception.Message}", "ส่งไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

