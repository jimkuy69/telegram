using System.ComponentModel;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.App.ViewModels;

/// <summary>
/// One row of the header account switcher. Rows are updated in place so the selection and an
/// open dropdown survive the periodic refresh, and only the TDLib connection state changes.
/// </summary>
public sealed class AccountSummary : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public AccountSummary(Account account)
    {
        Id = account.Id;
        DisplayName = account.DisplayName;
        Status = account.Status;
    }

    public long Id { get; }
    public string DisplayName { get; private set; }
    public string Status { get; private set; }
    public string BacklogText { get; private set; } = "ไม่มีงานค้าง";
    public bool IsConnected { get; private set; }
    public string StatusLine => $"{Status} • {BacklogText}";

    // UI Automation and screen readers fall back to this for the row name.
    public override string ToString() => $"{DisplayName} • {StatusLine}";

    public void Update(Account account, bool isConnected, int activeJobCount)
    {
        DisplayName = account.DisplayName;
        Status = account.Status;
        BacklogText = activeJobCount > 0 ? $"{activeJobCount} งานค้าง" : "ไม่มีงานค้าง";
        IsConnected = isConnected;
        foreach (var name in new[] { nameof(DisplayName), nameof(Status), nameof(BacklogText), nameof(IsConnected), nameof(StatusLine) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
