using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Security.Cryptography;
using TelegramControlStudio.Core.Import;
using Microsoft.Win32;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App;

public partial class TemplateEditorWindow : Window
{
    private readonly string _databasePath;
    private readonly long _accountId;
    private string? _selectedMediaPath;
    public TemplateEditorWindow(string databasePath, long accountId, string? body = null, string? link = null)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _accountId = accountId;
        BodyBox.Text = body ?? string.Empty;
        LinkBox.Text = link ?? string.Empty;
        BodyBox.TextChanged += (_, _) => UpdatePreview();
        LinkBox.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => UpdatePreview();
    private void ChooseMedia_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Media (*.jpg;*.jpeg;*.png;*.webp;*.pdf)|*.jpg;*.jpeg;*.png;*.webp;*.pdf", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        var mime = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", ".pdf" => "application/pdf", _ => "" };
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "media");
        var validation = MediaFileValidator.Validate(Path.Combine(root, Path.GetFileName(dialog.FileName)), mime, new FileInfo(dialog.FileName).Length, root);
        if (!validation.IsValid) { MessageBox.Show(validation.ErrorCode, "Media ไม่ผ่านการตรวจ"); return; }
        Directory.CreateDirectory(root);
        _selectedMediaPath = Path.Combine(root, Path.GetFileName(dialog.FileName));
        File.Copy(dialog.FileName, _selectedMediaPath, true);
        MediaText.Text = $"เลือกแล้ว: {Path.GetFileName(dialog.FileName)}";
    }
    private void UpdatePreview()
    {
        PreviewText.Text = BodyBox.Text;
        PreviewLink.Text = LinkBox.Text;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(BodyBox.Text))
        {
            MessageBox.Show("กรุณาระบุชื่อและข้อความ", "ข้อมูลไม่ครบ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var template = new MessageTemplate(0, _accountId, NameBox.Text.Trim(), ((ComboBoxItem)LanguageBox.SelectedItem).Content.ToString() ?? "th", BodyBox.Text, string.IsNullOrWhiteSpace(LinkBox.Text) ? null : LinkBox.Text.Trim(), 1, true, DateTimeOffset.UtcNow);
        var id = await new TemplateRepository(_databasePath).CreateNextVersionAsync(template);
        if (_selectedMediaPath is not null)
        {
            var bytes = await File.ReadAllBytesAsync(_selectedMediaPath);
            var mime = Path.GetExtension(_selectedMediaPath).ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", ".pdf" => "application/pdf", _ => "application/octet-stream" };
            var mediaRepository = new MediaRepository(_databasePath);
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
            var existing = await mediaRepository.FindBySha256Async(_accountId, sha256);
            var mediaId = existing?.Id ?? await mediaRepository.CreateAsync(new MediaFile(0, _accountId, Path.GetFileName(_selectedMediaPath), _selectedMediaPath, sha256, mime, bytes.LongLength, DateTimeOffset.UtcNow, null));
            await new TemplateMediaRepository(_databasePath).LinkAsync(new TemplateMedia(id, mediaId, _accountId, 0));
            await new ActivityLogRepository(_databasePath).AppendAsync(new ActivityLog(
                _accountId,
                null,
                "Info",
                "TemplateMediaLinked",
                $"Media SHA-256 {sha256} linked to template_id={id}.",
                DateTimeOffset.UtcNow));
        }
        MessageBox.Show($"บันทึก Template แล้ว (ID {id})", "สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}