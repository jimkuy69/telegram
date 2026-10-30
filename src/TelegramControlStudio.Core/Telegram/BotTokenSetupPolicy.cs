namespace TelegramControlStudio.Core.Telegram;

public enum BotTokenSetupDecision
{
    /// <summary>The submitted token may be stored.</summary>
    Save,

    /// <summary>A working token is already configured; the submitted one is ignored.</summary>
    AlreadyConfigured,

    /// <summary>The stored token no longer works, but replacing it needs the previous token as proof.</summary>
    RequiresPreviousToken,
}

/// <summary>
/// Decides whether a submitted bot token may replace the stored one. Replacing is only allowed
/// when the stored token no longer authenticates with Telegram, and only for the caller that can
/// either prove it knows that dead token or is setting up a system with no accounts yet.
/// </summary>
public static class BotTokenSetupPolicy
{
    public static BotTokenSetupDecision Decide(
        string? storedToken,
        string submittedToken,
        string? previousToken,
        bool storedTokenAccepted,
        bool hasUsers)
    {
        if (storedToken is null) return BotTokenSetupDecision.Save;
        if (string.Equals(storedToken, submittedToken, StringComparison.Ordinal)) return BotTokenSetupDecision.Save;
        if (storedTokenAccepted) return BotTokenSetupDecision.AlreadyConfigured;
        if (!hasUsers) return BotTokenSetupDecision.Save;

        return string.Equals(previousToken, storedToken, StringComparison.Ordinal)
            ? BotTokenSetupDecision.Save
            : BotTokenSetupDecision.RequiresPreviousToken;
    }
}
