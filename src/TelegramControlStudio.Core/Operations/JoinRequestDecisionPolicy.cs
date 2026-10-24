namespace TelegramControlStudio.Core.Operations;

public static class JoinRequestDecisionPolicy
{
    public static string Validate(string decision, string reason, bool confirmed)
    {
        if (!confirmed) return "CONFIRMATION_REQUIRED";
        if (decision is not ("Approved" or "Rejected")) return "INVALID_DECISION";
        return string.IsNullOrWhiteSpace(reason) ? "REASON_REQUIRED" : string.Empty;
    }
}

