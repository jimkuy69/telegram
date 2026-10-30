using System.Windows;
using System.Windows.Controls;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.App.ViewModels;

namespace TelegramControlStudio.App;

/// <summary>
/// The trail of opt-in requests and the answers that came back. It shows the matched keyword and the
/// timestamp rather than what the recipient wrote, because consent evidence has to survive without
/// keeping anyone's chat text.
/// </summary>
public partial class ConsentInboxWindow : Window
{
    private static readonly (string? Value, string Label)[] Filters =
    [
        (null, "ทุกรายการ"),
        (ConsentRequestKinds.Requested, "รอคำตอบ"),
        (ConsentRequestKinds.Granted, "ได้ Consent"),
        (ConsentRequestKinds.Rejected, "ตอบปฏิเสธ"),
        (ConsentRequestKinds.OptedOut, "สั่งหยุดส่ง")
    ];

    private readonly string _databasePath;
    private readonly long _accountId;

    public ConsentInboxWindow(string databasePath, long accountId)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _accountId = accountId;
        KindBox.ItemsSource = Filters.Select(filter => new FilterChoice(filter.Value, filter.Label)).ToArray();
        KindBox.SelectedIndex = 0;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var kind = (KindBox.SelectedItem as FilterChoice)?.Value;
        var events = await new ConsentRequestRepository(_databasePath).ListAsync(_accountId, kind);
        var names = (await new ContactRepository(_databasePath).ListAsync(_accountId))
            .ToDictionary(contact => contact.TelegramUserId,
                contact => $"{contact.FirstName} {contact.LastName}".Trim() is { Length: > 0 } name ? name : $"@{contact.TelegramUserId}");

        EventsGrid.ItemsSource = events.Select(item => new EventRow(
            item.TelegramUserId,
            names.TryGetValue(item.TelegramUserId, out var name) ? name : item.TelegramUserId.ToString(),
            ConsentRequestDisplay.DescribeEventKind(item.Kind),
            item.OccurredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
            string.IsNullOrWhiteSpace(item.MatchedKeyword) ? "-" : item.MatchedKeyword,
            ConsentRequestDisplay.DescribeSource(item.Source))).ToArray();

        var latest = await new ConsentRequestRepository(_databasePath).LatestByContactAsync(_accountId);
        SummaryText.Text = $"บันทึกทั้งหมด {events.Count} รายการ • ผู้ติดต่อที่มีประวัติถูกถาม {latest.Count} คน • " +
                           $"รอคำตอบ {latest.Count(entry => entry.Value.Kind == ConsentRequestKinds.Requested)} คน • " +
                           $"ให้ Consent แล้ว {latest.Count(entry => entry.Value.Kind == ConsentRequestKinds.Granted)} คน";
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) _ = LoadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
        StatusText.Text = $"โหลดล่าสุด {DateTime.Now:HH:mm:ss}";
    }

    private void AskAgain_Click(object sender, RoutedEventArgs e)
    {
        var userIds = (EventsGrid.ItemsSource as IEnumerable<EventRow>)?
            .Where(row => EventsGrid.SelectedItems.Contains(row))
            .Select(row => row.TelegramUserId)
            .Distinct()
            .ToArray() ?? [];
        if (userIds.Length == 0)
        {
            MessageBox.Show("กรุณาเลือกรายการที่ต้องการถามซ้ำก่อน", "ยังไม่ได้เลือก", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new ConsentRequestWindow(_databasePath, _accountId, userIds) { Owner = this }.ShowDialog();
        _ = LoadAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record FilterChoice(string? Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record EventRow(
        long TelegramUserId,
        string DisplayName,
        string Kind,
        string OccurredAt,
        string MatchedKeyword,
        string Source);
}
