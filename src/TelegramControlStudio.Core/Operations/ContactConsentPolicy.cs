namespace TelegramControlStudio.Core.Operations;

public enum ContactConsentState
{
    NotAllowed = 0,
    Consented = 1,
    OptedOut = 2
}

public static class ContactConsentPolicy
{
    public const string ConsentedLabel = "ได้รับความยินยอม";
    public const string NotAllowedLabel = "ยังไม่อนุญาต";
    public const string OptedOutLabel = "Opt-out";

    public static ContactConsentState Classify(bool hasConsent, bool isOptedOut) =>
        isOptedOut ? ContactConsentState.OptedOut
        : hasConsent ? ContactConsentState.Consented
        : ContactConsentState.NotAllowed;

    public static string Label(ContactConsentState state) => state switch
    {
        ContactConsentState.Consented => ConsentedLabel,
        ContactConsentState.OptedOut => OptedOutLabel,
        _ => NotAllowedLabel
    };

    public static int SortRank(ContactConsentState state) => state switch
    {
        ContactConsentState.Consented => 0,
        ContactConsentState.NotAllowed => 1,
        _ => 2
    };
}
