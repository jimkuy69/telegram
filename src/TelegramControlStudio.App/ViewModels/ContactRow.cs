using System.ComponentModel;
using TelegramControlStudio.Core.Operations;

namespace TelegramControlStudio.App.ViewModels;

public sealed class ContactRow : INotifyPropertyChanged
{
    private bool _selected;

    public ContactRow(bool selected, string name, long id, string username, ContactConsentState consentState)
    {
        _selected = selected;
        Name = name;
        Id = id;
        Username = username;
        ConsentState = consentState;
        Consent = ContactConsentPolicy.Label(consentState);
        Initials = BuildInitials(name);
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    public string Name { get; }
    public long Id { get; }
    public string Username { get; }
    public ContactConsentState ConsentState { get; }
    public string Consent { get; }
    public string Initials { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static string BuildInitials(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return "?";
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return string.Concat(parts[0][0], parts[1][0]).ToUpperInvariant();
        return trimmed.Length >= 2 ? trimmed[..2].ToUpperInvariant() : trimmed.ToUpperInvariant();
    }
}
