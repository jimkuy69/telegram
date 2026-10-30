using TelegramControlStudio.Core.Safety;

namespace TelegramControlStudio.Core.Domain;

/// <summary>
/// How often the app may ask people for consent. Kept small on purpose: an opt-in campaign that
/// runs away is the fastest way to get reported, so the limits are stored, editable, and read back
/// from the request trail rather than remembered in memory.
/// </summary>
public sealed record ConsentRequestSettings(int CooldownDays, int DailyLimit)
{
    public static ConsentRequestSettings Default { get; } = new(
        ConsentRequestPolicy.DefaultCooldownDays,
        ConsentRequestPolicy.DefaultDailyLimit);
}
