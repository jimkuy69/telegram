namespace TelegramControlStudio.Web.Services;

using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Infrastructure.Hosting;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Safety;

public sealed class WorkspaceState(StudioHost studio)
{
    private readonly Dictionary<long, Contact> _selected = [];

    public string DatabasePath => studio.DatabasePath;
    public IReadOnlyList<Account> Accounts { get; private set; } = [];
    public Account? ActiveAccount { get; private set; }
    public SendSafetySettings Safety { get; private set; } = SendSafetySettings.SafeDefault;
    public IReadOnlyList<Contact> SelectedContacts =>
        _selected.Values.OrderBy(contact => contact.FirstName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    public int SelectedCount => _selected.Count;
    public int SelectedConsentedCount => _selected.Values.Count(contact => contact.HasConsent && !contact.IsOptedOut);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Accounts = await new AccountRepository(DatabasePath).ListAsync(cancellationToken);
        if (ActiveAccount is null || Accounts.All(account => account.Id != ActiveAccount.Id))
        {
            ActiveAccount = Accounts.FirstOrDefault();
            _selected.Clear();
        }
        await ReloadSafetyAsync(cancellationToken);
    }

    public void SetActiveAccount(long accountId)
    {
        if (ActiveAccount?.Id == accountId) return;
        ActiveAccount = Accounts.FirstOrDefault(account => account.Id == accountId);
        _selected.Clear();
    }

    public bool IsSelected(long telegramUserId) => _selected.ContainsKey(telegramUserId);

    public void SetSelected(Contact contact, bool selected)
    {
        if (selected) _selected[contact.TelegramUserId] = contact;
        else _selected.Remove(contact.TelegramUserId);
    }

    public void Toggle(Contact contact) => SetSelected(contact, !IsSelected(contact.TelegramUserId));

    public void SelectMany(IEnumerable<Contact> contacts)
    {
        foreach (var contact in contacts) _selected[contact.TelegramUserId] = contact;
    }

    public void ClearSelection() => _selected.Clear();

    public async Task ReloadSafetyAsync(CancellationToken cancellationToken = default) =>
        Safety = await new SendSafetyRepository(DatabasePath).GetAsync(cancellationToken);

    public async Task SetKillSwitchAsync(bool engaged, CancellationToken cancellationToken = default)
    {
        await new SendSafetyRepository(DatabasePath).SetKillSwitchAsync(engaged);
        if (engaged) EmergencyStopSignal.Trigger();
        await ReloadSafetyAsync(cancellationToken);
    }
}
