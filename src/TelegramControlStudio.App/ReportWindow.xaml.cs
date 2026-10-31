using System.IO;
using System.Text;
using System.Windows;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Export;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Core.Reporting;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.App.ViewModels;

namespace TelegramControlStudio.App;

/// <summary>
/// The date-range result report for one account: what was sent, what failed and why.
/// Numbers come from <see cref="ResultReportRepository"/>, which only reads, so opening this window
/// can never change a queue or a recipient.
/// </summary>
public partial class ReportWindow : Window
{
    private readonly string _databasePath;
    private readonly long _accountId;
    private readonly string _accountName;
    private ResultReport? _report;

    public ReportWindow(string databasePath, long accountId, string accountName)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _accountId = accountId;
        _accountName = accountName;
        ToDateBox.SelectedDate = DateTime.Today;
        FromDateBox.SelectedDate = DateTime.Today.AddDays(-6);
        Loaded += async (_, _) => await LoadAsync();
    }

    private (DateTimeOffset From, DateTimeOffset To) ResolveRange()
    {
        var first = (FromDateBox.SelectedDate ?? DateTime.Today.AddDays(-6)).Date;
        var last = (ToDateBox.SelectedDate ?? DateTime.Today).Date;
        if (last < first) (first, last) = (last, first);
        var from = new DateTimeOffset(first, TimeZoneInfo.Local.GetUtcOffset(first));
        var to = new DateTimeOffset(last.AddTicks(TimeSpan.TicksPerDay - 1), TimeZoneInfo.Local.GetUtcOffset(last));
        return (from, to);
    }

    private async Task LoadAsync()
    {
        try
        {
            var (from, to) = ResolveRange();
            var report = await new ResultReportRepository(_databasePath).LoadAsync(_accountId, from, to, TimeZoneInfo.Local);
            _report = report;
            Render(report, from, to);
            ExportButton.IsEnabled = true;
            StatusText.Text = $"โหลดล่าสุด {DateTime.Now:HH:mm:ss} • อ่านอย่างเดียว ไม่แก้คิวหรือรายชื่อ";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"โหลดรายงานไม่สำเร็จ: {exception.Message}";
        }
    }

    private void Render(ResultReport report, DateTimeOffset from, DateTimeOffset to)
    {
        SubtitleText.Text = $"บัญชี {report.AccountId} ({_accountName}) • " +
                            $"{from.ToLocalTime():dd/MM/yyyy} ถึง {to.ToLocalTime():dd/MM/yyyy} • " +
                            $"คิวส่งข้อความ {report.CampaignJobs} คิว • คิวคำขอยินยอม {report.ConsentJobs} คิว";

        CardsBox.ItemsSource = new[]
        {
            new CardRow("ส่งสำเร็จ", report.Sent.ToString(), $"ผู้รับที่ถูกตัดสินแล้ว {report.Settled} คน"),
            new CardRow("อัตราสำเร็จ", ReportDisplay.Percent(report.SuccessRate), "นับเฉพาะส่งสำเร็จ/ล้มเหลว/ข้าม"),
            new CardRow("ล้มเหลว", report.Failed.ToString(), report.Failed > 0 ? "ดูเหตุผลในตารางด้านขวา" : "ไม่มีรายการล้มเหลว"),
            new CardRow("ข้าม", report.Skipped.ToString(), "มักเกิดจาก Safety ประตูกั้น"),
            new CardRow("รอดำเนินการ", report.Pending.ToString(), "ค้างในคิว รอยืนยัน หรือรอ Flood wait"),
            new CardRow("ยกเลิก", report.Cancelled.ToString(), "ยกเลิกด้วยมือหรือหยุดฉุกเฉิน"),
            new CardRow("คำขอยินยอมที่ส่งไป", report.ConsentRequestsSent.ToString(), "นับจากรายการที่ส่งสำเร็จ")
        };

        DaysGrid.ItemsSource = report.Days
            .Select(day => new DayRow(day.Day.ToString("dd/MM/yyyy"), day.Sent, day.ConsentRequestsSent, day.Failed,
                day.Skipped, day.Cancelled, day.Pending, ReportDisplay.Percent(day.SuccessRate)))
            .ToArray();

        FailuresGrid.ItemsSource = report.Failures
            .Select(failure => new FailureRow(
                ReportDisplay.DescribeReason(failure.Reason),
                ReportDisplay.DescribeFailureKind(JobFailureClassifier.Classify(failure.Reason)),
                failure.Count,
                failure.Reason))
            .ToArray();

        var consent = report.Consent;
        ConsentText.Text = consent.Requested == 0
            ? "ช่วงนี้ยังไม่มีการบันทึกเหตุการณ์ความยินยอม"
            : $"ความยินยอมช่วงนี้: ถามไป {consent.Requested} • ได้ Consent {consent.Granted} • ตอบปฏิเสธ {consent.Rejected} • " +
              $"Opt-out {consent.OptedOut} • รอคำตอบ {consent.AwaitingAnswer} • อัตราได้รับ Consent {ReportDisplay.Percent(consent.GrantRate)}";
    }

    private IReadOnlyList<IReadOnlyList<string?>> BuildCsvRows(ResultReport report)
    {
        var rows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "Telegram Control Studio", "รายงานผลตามช่วงวันที่" },
            new string?[] { "บัญชี", report.AccountId.ToString(), _accountName },
            new string?[] { "ช่วงวันที่", report.From.ToLocalTime().ToString("dd/MM/yyyy"), report.To.ToLocalTime().ToString("dd/MM/yyyy") },
            Array.Empty<string?>(),
            new string?[] { "ภาพรวม", "จำนวน" },
            new string?[] { "คิวส่งข้อความ", report.CampaignJobs.ToString() },
            new string?[] { "คิวคำขอยินยอม", report.ConsentJobs.ToString() },
            new string?[] { "รายการผู้รับทั้งหมดในช่วง", report.TotalItems.ToString() },
            new string?[] { "ส่งสำเร็จ", report.Sent.ToString() },
            new string?[] { "อัตราสำเร็จ", ReportDisplay.Percent(report.SuccessRate) },
            new string?[] { "ล้มเหลว", report.Failed.ToString() },
            new string?[] { "ข้าม", report.Skipped.ToString() },
            new string?[] { "รอดำเนินการ", report.Pending.ToString() },
            new string?[] { "ยกเลิก", report.Cancelled.ToString() },
            new string?[] { "คำขอยินยอมที่ส่งสำเร็จ", report.ConsentRequestsSent.ToString() },
            Array.Empty<string?>(),
            new string?[] { "ผลรายวัน", "ส่งสำเร็จ", "คำขอยินยอม", "ล้มเหลว", "ข้าม", "ยกเลิก", "รอดำเนินการ", "อัตราสำเร็จ" }
        };

        foreach (var day in report.Days)
            rows.Add(new string?[]
            {
                day.Day.ToString("dd/MM/yyyy"), day.Sent.ToString(), day.ConsentRequestsSent.ToString(), day.Failed.ToString(),
                day.Skipped.ToString(), day.Cancelled.ToString(), day.Pending.ToString(), ReportDisplay.Percent(day.SuccessRate)
            });

        rows.Add(Array.Empty<string?>());
        rows.Add(new string?[] { "เหตุผลที่ต้องแก้ไข", "ประเภท", "จำนวน", "รหัส" });
        foreach (var failure in report.Failures)
            rows.Add(new string?[]
            {
                ReportDisplay.DescribeReason(failure.Reason),
                ReportDisplay.DescribeFailureKind(JobFailureClassifier.Classify(failure.Reason)),
                failure.Count.ToString(), failure.Reason
            });

        var consent = report.Consent;
        rows.Add(Array.Empty<string?>());
        rows.Add(new string?[] { "ความยินยอม", "จำนวน" });
        rows.Add(new string?[] { ReportDisplay.DescribeConsentKind(ConsentRequestKinds.Requested), consent.Requested.ToString() });
        rows.Add(new string?[] { ReportDisplay.DescribeConsentKind(ConsentRequestKinds.Granted), consent.Granted.ToString() });
        rows.Add(new string?[] { ReportDisplay.DescribeConsentKind(ConsentRequestKinds.Rejected), consent.Rejected.ToString() });
        rows.Add(new string?[] { ReportDisplay.DescribeConsentKind(ConsentRequestKinds.OptedOut), consent.OptedOut.ToString() });
        rows.Add(new string?[] { "รอคำตอบ", consent.AwaitingAnswer.ToString() });
        return rows;
    }

    private void Preset7_Click(object sender, RoutedEventArgs e)
    {
        ToDateBox.SelectedDate = DateTime.Today;
        FromDateBox.SelectedDate = DateTime.Today.AddDays(-6);
        _ = LoadAsync();
    }

    private void Preset30_Click(object sender, RoutedEventArgs e)
    {
        ToDateBox.SelectedDate = DateTime.Today;
        FromDateBox.SelectedDate = DateTime.Today.AddDays(-29);
        _ = LoadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_report is not { } report)
        {
            MessageBox.Show("กรุณากดดูรายงานก่อนส่งออก", "ยังไม่มีข้อมูล", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var fileName = $"tcs-report-{report.AccountId}-{report.From.ToLocalTime():yyyyMMdd}-{report.To.ToLocalTime():yyyyMMdd}.csv";
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = fileName };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, CsvExporter.Rows(BuildCsvRows(report)), new UTF8Encoding(true));
            MessageBox.Show($"บันทึก CSV ลงที่\n{dialog.FileName}", "Export สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"ส่งออกไม่สำเร็จ\n{exception.Message}", "Export ไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record CardRow(string Label, string Value, string Hint);

    private sealed record DayRow(string Day, int Sent, int ConsentSent, int Failed, int Skipped, int Cancelled, int Pending, string SuccessRate);

    private sealed record FailureRow(string Label, string Kind, int Count, string Code);
}
