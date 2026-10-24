using System.Windows;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App;

public partial class MediaLibraryWindow : Window
{
    private readonly string _databasePath;
    private readonly long _accountId;

    public MediaLibraryWindow(string databasePath, long accountId)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _accountId = accountId;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var media = await new MediaRepository(_databasePath).ListAsync(_accountId);
        ApplyFilter(media);
    }

    private void ApplyFilter(IReadOnlyList<MediaFile> media)
    {
        var query = SearchBox.Text.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? media
            : media.Where(item => item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.MimeType.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Sha256.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        MediaGrid.ItemsSource = filtered;
        CountText.Text = $"แสดง {filtered.Count} จาก {media.Count} ไฟล์";
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = MediaGrid.SelectedItems.OfType<MediaFile>().ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show("กรุณาเลือกไฟล์ก่อน", "ยังไม่ได้เลือก", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"ยืนยัน Soft Delete {selected.Length} ไฟล์?", "ยืนยัน", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var repository = new MediaRepository(_databasePath);
        var logs = new ActivityLogRepository(_databasePath);
        foreach (var media in selected)
        {
            await repository.SoftDeleteAsync(_accountId, media.Id);
            await logs.AppendAsync(new ActivityLog(_accountId, null, "Info", "MediaSoftDeleted", $"Media soft-deleted media_id={media.Id}.", DateTimeOffset.UtcNow));
        }
        await LoadAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}