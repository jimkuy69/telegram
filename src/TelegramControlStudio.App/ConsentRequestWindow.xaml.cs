using System.Windows;
using System.Windows.Controls;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.App.ViewModels;

namespace TelegramControlStudio.App;

/// <summary>
/// Reviews an opt-in request before it becomes a job. The plan is computed by the same gate the
/// runner applies at send time, so what the operator confirms here is what can really go out —
/// and nothing is sent until the job is confirmed in the Queue window.
/// </summary>
public partial class ConsentRequestWindow : Window
{
    private readonly string _databasePath;
    private readonly long _accountId;
    private readonly IReadOnlyCollection<long> _selectedUserIds;
    private IReadOnlyList<MessageTemplate> _templates = [];

    public ConsentRequestWindow(string databasePath, long accountId, IReadOnlyCollection<long> selectedUserIds)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _accountId = accountId;
        _selectedUserIds = selectedUserIds;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _templates = await new TemplateRepository(_databasePath).ListAsync(_accountId, activeOnly: true);
        var choices = new List<TemplateChoice> { new(null, "ไม่ใช้เทมเพลต") };
        choices.AddRange(_templates.Select(template => new TemplateChoice(
            template.Id,
            $"{template.Name} v{template.Version}")));
        TemplateBox.ItemsSource = choices;
        TemplateBox.SelectedIndex = 0;

        if (string.IsNullOrWhiteSpace(BodyBox.Text))
            BodyBox.Text = DefaultBody;

        await RefreshPlanAsync();
    }

    private async Task RefreshPlanAsync()
    {
        var plan = await new ConsentRequestQueueService(_databasePath).PlanAsync(_accountId, _selectedUserIds);
        EligibleGrid.ItemsSource = plan.Eligible.Select(candidate => new CandidateRow(
            candidate.TelegramUserId, candidate.DisplayName, ConsentRequestDisplay.DescribeReason(candidate.ReasonCode))).ToArray();
        ExcludedGrid.ItemsSource = plan.Excluded.Select(candidate => new CandidateRow(
            candidate.TelegramUserId, candidate.DisplayName, ConsentRequestDisplay.DescribeReason(candidate.ReasonCode))).ToArray();

        EligibleHeader.Text = $"ถามได้ {plan.Eligible.Count} รายการ";
        ExcludedHeader.Text = $"ถูกข้าม {plan.Excluded.Count} รายการ";
        SummaryText.Text = $"เลือกไว้ {_selectedUserIds.Count} คน • โควตาเหลือวันนี้ {plan.RemainingToday} จาก {plan.DailyLimit} • " +
                           $"ถามคนเดิมได้ทุก {plan.CooldownDays} วัน • คำขอจะถูกเข้าคิวและยังไม่ต้องส่งจนกว่าจะกดยืนยันในหน้าต่างคิวงาน";
        PlanText.Text = plan.Eligible.Count == 0
            ? "ยังไม่มีผู้รับที่ถามได้ — ดูเหตุผลในช่องด้านขวา"
            : $"ระบบจะส่งคำขอ {plan.Eligible.Count} รายการ (ไม่เกินโควตา {plan.RemainingToday} รายการที่เหลือของวันนี้)";

        var warnings = new List<string>();
        if (plan.KillSwitchEngaged) warnings.Add("หยุดฉุกเฉิน (Kill switch) กำลังทำงาน — จะไม่มีการส่งใด ๆ จนกว่าจะปลด");
        if (plan.TestModeEnabled) warnings.Add("โหมดทดสอบเปิดอยู่ — ผู้รับนอก allowlist จะถูกข้าม");
        if (plan.DailyLimit == 0) warnings.Add("โควตาประจำวันตั้งเป็น 0 จึงยังถามใครไม่ได้ (แก้ได้ที่หน้าตั้งค่า)");
        SafetyWarningText.Text = string.Join("  •  ", warnings);

        QueueButton.IsEnabled = plan.Eligible.Count > 0;
    }

    private void Template_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateBox.SelectedItem is not TemplateChoice choice || choice.TemplateId is not { } templateId) return;
        var template = _templates.FirstOrDefault(item => item.Id == templateId);
        if (template is null) return;
        BodyBox.Text = template.Body;
    }

    private async void Queue_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmBox.IsChecked.GetValueOrDefault())
        {
            MessageBox.Show("กรุณาติ๊กกล่องยืนยันก่อนสร้างคิวคำขอ", "ยังไม่ได้ยืนยัน", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(BodyBox.Text))
        {
            MessageBox.Show("กรุณากรอกข้อความที่จะใช้ขอความยินยอม", "ข้อมูลไม่ครบ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var templateId = (TemplateBox.SelectedItem as TemplateChoice)?.TemplateId;
        try
        {
            var result = await new ConsentRequestQueueService(_databasePath).QueueAsync(
                _accountId, _selectedUserIds, BodyBox.Text, templateId, confirmed: true);
            if (!result.Created)
            {
                StatusText.Text = "ไม่มีรายการที่ถามได้ จึงไม่ได้สร้างคิวงาน — ตรวจเหตุผลในช่องด้านขวา";
                await RefreshPlanAsync();
                return;
            }

            StatusText.Text = $"สร้างคิวงาน {result.JobId} เรียบร้อย ({result.Plan.Eligible.Count} รายการ) — " +
                              "ไปกดยืนยันในหน้าต่างคิวงานเพื่อเริ่มส่ง ระบบจะเว้นระยะระหว่างรายการและหยุดทันทีเมื่อมี FloodWait";
            ConfirmBox.IsChecked = false;
            await RefreshPlanAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show($"สร้างคิวคำขอไม่สำเร็จ\n{exception.Message}", "ตรวจสอบอีกครั้ง", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenQueue_Click(object sender, RoutedEventArgs e) =>
        new QueueWindow(_databasePath, _accountId) { Owner = this }.ShowDialog();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private const string DefaultBody =
        "สวัสดีครับ ผมเก็บเบอร์ของคุณไว้ในรายชื่อผู้ติดต่อ และอยากส่งข้อมูลเกี่ยวกับบริการของเราให้ทราบ " +
        "หากคุณยินดีรับข้อความ ตอบกลับว่า \"ตกลง\" ครับ หากไม่ต้องการ สามารถตอบ \"หยุดส่งข้อความ\" ได้ตลอด";

    private sealed record TemplateChoice(long? TemplateId, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record CandidateRow(long TelegramUserId, string DisplayName, string Reason);
}
