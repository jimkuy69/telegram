using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TelegramControlStudio.App.ViewModels;

public sealed class ContactRow : INotifyPropertyChanged
{
    private bool _selected;

    public ContactRow(bool selected, string name, long id, string username, string consent)
    {
        _selected = selected;
        Name = name;
        Id = id;
        Username = username;
        Consent = consent;
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
    public string Consent { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

