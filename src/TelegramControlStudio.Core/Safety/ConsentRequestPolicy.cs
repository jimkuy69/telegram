namespace TelegramControlStudio.Core.Safety;

/// <summary>
/// Everything the rules need to know about one recipient before an opt-in request is sent.
/// Cooldown and daily usage are read from the persisted request trail instead of trusted from
/// the caller, so a retry or a second window cannot push the same person twice.
/// </summary>
public sealed record ConsentRequestEligibility(
    long TelegramUserId,
    bool HasOwnPhoneNumber,
    bool AlreadyConsented,
    bool IsOptedOut,
    DateTimeOffset? LastRequestedAt,
    int RequestedToday,
    int CooldownDays,
    int DailyLimit,
    DateTimeOffset Now,
    bool PreviouslyDeclined = false);

/// <summary>
/// Who may be asked for consent at all. An opt-in request is the one message that is sent to a
/// person who has not consented yet, so it is deliberately narrower than campaign sending:
/// only numbers the operator collected themselves, never an opted-out person, and rate limited.
/// </summary>
public static class ConsentRequestPolicy
{
    public const int DefaultCooldownDays = 30;
    public const int DefaultDailyLimit = 20;

    public const string AllowedReason = "CONSENT_REQUEST_ALLOWED";
    public const string NoOwnNumberReason = "CONSENT_REQUEST_NO_OWN_NUMBER";
    public const string AlreadyConsentedReason = "CONSENT_REQUEST_ALREADY_CONSENTED";
    public const string OptedOutReason = "CONSENT_REQUEST_OPTED_OUT";
    public const string CooldownReason = "CONSENT_REQUEST_COOLDOWN";
    public const string DailyLimitReason = "CONSENT_REQUEST_DAILY_LIMIT";
    public const string DeclinedReason = "CONSENT_REQUEST_PREVIOUSLY_DECLINED";

    public static SendSafetyDecision Evaluate(ConsentRequestEligibility eligibility)
    {
        ArgumentNullException.ThrowIfNull(eligibility);
        if (!eligibility.HasOwnPhoneNumber)
            return new(SendSafetyVerdict.Skip, NoOwnNumberReason);
        // Opt-out outranks consent here too, matching ContactConsentPolicy: someone who said stop
        // must not be re-asked just because an older reply said yes.
        if (eligibility.IsOptedOut)
            return new(SendSafetyVerdict.Skip, OptedOutReason);
        if (eligibility.AlreadyConsented)
            return new(SendSafetyVerdict.Skip, AlreadyConsentedReason);
        // A person who already answered "no" is not asked again on a timer; their answer stands
        // until they change it themselves.
        if (eligibility.PreviouslyDeclined)
            return new(SendSafetyVerdict.Skip, DeclinedReason);
        if (eligibility.LastRequestedAt is { } lastRequested &&
            lastRequested > eligibility.Now.AddDays(-Math.Max(0, eligibility.CooldownDays)))
            return new(SendSafetyVerdict.Skip, CooldownReason);
        if (eligibility.DailyLimit <= 0 || eligibility.RequestedToday >= eligibility.DailyLimit)
            return new(SendSafetyVerdict.Skip, DailyLimitReason);
        return new(SendSafetyVerdict.Allow, AllowedReason);
    }
}
