namespace TelegramControlStudio.Core.Domain;

/// <summary>
/// What removing one account actually deleted, so the operator can read back the blast radius
/// instead of trusting a generic success message.
/// </summary>
public sealed record AccountDeletion(long AccountId, string DisplayName, IReadOnlyDictionary<string, int> DeletedRows)
{
    public int TotalRows => DeletedRows.Values.Sum();
}
