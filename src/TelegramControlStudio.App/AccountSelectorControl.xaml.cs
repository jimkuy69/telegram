using System.Windows.Controls;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App;

public partial class AccountSelectorControl : UserControl
{
    public event EventHandler<long>? AccountChanged;

    private bool _syncing;

    public AccountSelectorControl()
    {
        InitializeComponent();
    }

    public void Initialize(string databasePath, long accountId)
    {
        _ = LoadAsync(databasePath, accountId);
    }

    private async Task LoadAsync(string databasePath, long accountId)
    {
        try
        {
            var accounts = await new AccountRepository(databasePath).ListAsync();
            _syncing = true;
            AccountBox.ItemsSource = accounts
                .Select(account => new AccountOption(account.Id, $"{account.DisplayName} • {account.Status}"))
                .ToArray();
            AccountBox.SelectedValue = accountId;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void AccountBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        if (AccountBox.SelectedValue is long accountId) AccountChanged?.Invoke(this, accountId);
    }

    private sealed record AccountOption(long Id, string Label)
    {
        public override string ToString() => Label;
    }
}
