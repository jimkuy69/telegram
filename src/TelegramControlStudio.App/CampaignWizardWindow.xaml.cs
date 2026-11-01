using System.Windows;
using System.Windows.Controls;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.App;

public partial class CampaignWizardWindow : Window
{
    private readonly string _databasePath;
    private long _accountId;
    private IReadOnlyList<Contact> _contacts = [];
    private bool _loadingTemplates;
    public CampaignWizardWindow(string databasePath, long accountId)
    {
        InitializeComponent(); _databasePath = databasePath; _accountId = accountId;
        AccountSelector.Initialize(databasePath, accountId);
        AccountSelector.AccountChanged += OnAccountChanged;
        Loaded += async (_, _) => await ReloadContactsAsync();
    }
    private async void OnAccountChanged(object? sender, long accountId)
    {
        _accountId = accountId;
        await ReloadContactsAsync();
    }
    private async Task ReloadContactsAsync()
    {
        _contacts = await new ContactRepository(_databasePath).ListAsync(_accountId);
        RecipientSummary.Text = $"ผู้รับที่ผ่าน Consent: {_contacts.Count(item => item.HasConsent && !item.IsOptedOut)} รายการ";
        await ReloadTemplatesAsync();
    }

    // Templates are written by the Template Editor window, so the list is read again every time the
    // dropdown opens instead of only when this window starts.
    private async Task ReloadTemplatesAsync()
    {
        _loadingTemplates = true;
        var selected = (TemplateBox.SelectedItem as TemplateOption)?.Template;
        var templates = await new TemplateRepository(_databasePath).ListAsync(_accountId, activeOnly: false);
        TemplateBox.ItemsSource = templates.Select(template => new TemplateOption(template)).ToArray();
        TemplateBox.SelectedItem = templates.Count == 0
            ? null
            : TemplateBox.Items.OfType<TemplateOption>().FirstOrDefault(option => option.Template.Id == (selected?.Id ?? templates[0].Id));
        TemplateHint.Text = templates.Count == 0
            ? "ยังไม่มีเทมเพลตในบัญชีนี้ — สร้างไว้ในเมนู Template Editor ก่อน"
            : $"มีเทมเพลต {templates.Count} รายการ";
        _loadingTemplates = false;
        // Suppressing SelectionChanged keeps an edited draft from being overwritten on refresh, so the
        // first template has to fill an empty box here or the operator sees a blank message.
        if (TemplateBox.SelectedItem is TemplateOption option && string.IsNullOrWhiteSpace(MessageTextBox.Text))
            MessageTextBox.Text = option.Template.Body;
    }

    private async void Templates_DropDownOpened(object sender, EventArgs e) => await ReloadTemplatesAsync();

    private void TemplateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTemplates || TemplateBox.SelectedItem is not TemplateOption option) return;
        // Creating a campaign reads the body from the database, so the box shows the same text the
        // campaign will send rather than an unrelated draft.
        MessageTextBox.Text = option.Template.Body;
        TemplateHint.Text = string.IsNullOrWhiteSpace(option.Template.Link)
            ? $"เทมเพลต ID {option.Template.Id} • เวอร์ชัน {option.Template.Version}"
            : $"เทมเพลต ID {option.Template.Id} • เวอร์ชัน {option.Template.Version} • มีลิงก์ {option.Template.Link}";
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateBox.SelectedItem is not TemplateOption option || string.IsNullOrWhiteSpace(NameBox.Text))
        { MessageBox.Show("กรุณาระบุชื่อและเลือกเทมเพลต", "ข้อมูลไม่ครบ"); return; }
        if (MessageBox.Show("ยืนยันการสร้าง Campaign หรือไม่? ระบบยังไม่ส่งข้อความจริง", "ยืนยัน", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var template = option.Template;
            var id = await new CampaignBuilder(_databasePath).CreateFromConsentedContactsAsync(_accountId, template.Id, NameBox.Text.Trim(), PolicyBox.Text.Trim(), _contacts, true);
            var jobId = await new CampaignQueueService(_databasePath).QueueCampaignAsync(id, _accountId, template.Body, template.Link, true);
            MessageBox.Show($"สร้าง Campaign และเข้าคิว Job แล้ว\nCampaign: {id}\nJob: {jobId}\n\nระบบยังไม่ส่งข้อความจริง", "สำเร็จ");
        }
        catch (Exception exception)
        {
            MessageBox.Show($"สร้าง Campaign ไม่สำเร็จ\n{exception.Message}", "ตรวจสอบข้อมูล", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateBox.SelectedItem is not TemplateOption option || string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(MessageTextBox.Text))
        { MessageBox.Show("กรุณาระบุชื่อ เลือกเทมเพลต และข้อความ", "ข้อมูลไม่ครบ"); return; }
        var app = (App)Application.Current;
        if (!app.TdLibClients.TryGet(_accountId, out var client) || client is null)
        { MessageBox.Show("บัญชีนี้ยังไม่ได้เชื่อมต่อและยืนยันตัวตนกับ TDLib", "ยังไม่พร้อมส่ง"); return; }
        var eligible = _contacts.Count(item => item.HasConsent && !item.IsOptedOut);
        if (eligible == 0) { MessageBox.Show("ไม่มีผู้รับที่ผ่าน Consent", "ส่งไม่ได้"); return; }
        if (MessageBox.Show($"ยืนยันส่งข้อความจริงให้ผู้รับที่มี Consent จำนวน {eligible} รายการ?\nระบบจะส่งทีละรายการและข้าม Opt-out", "ยืนยันการส่งจริง", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var text = MessageTextBox.Text.Trim();
            var id = await new CampaignBuilder(_databasePath).CreateFromConsentedContactsAsync(_accountId, option.Template.Id, NameBox.Text.Trim(), PolicyBox.Text.Trim(), _contacts, true);
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

    private sealed record TemplateOption(MessageTemplate Template)
    {
        public override string ToString() => $"{Template.Name} (v{Template.Version})";
    }
}
