using System.Windows;
using System.Windows.Controls;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App;

public partial class TemplateEditorWindow : Window
{
    private readonly string _databasePath;
    private readonly long _accountId;
    public TemplateEditorWindow(string databasePath, long accountId)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _accountId = accountId;
        BodyBox.TextChanged += (_, _) => UpdatePreview();
        LinkBox.TextChanged += (_, _) => UpdatePreview();
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => UpdatePreview();
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
        MessageBox.Show($"บันทึก Template แล้ว (ID {id})", "สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

