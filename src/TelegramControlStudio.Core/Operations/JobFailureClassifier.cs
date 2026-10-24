namespace TelegramControlStudio.Core.Operations;

public static class JobFailureClassifier
{
    public static JobFailureKind Classify(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return JobFailureKind.Permanent;
        var value = error.ToUpperInvariant();
        if (value.Contains("FLOOD_WAIT_", StringComparison.Ordinal)) return JobFailureKind.FloodWait;
        if (value.Contains("TIMEOUT", StringComparison.Ordinal) ||
            value.Contains("NETWORK", StringComparison.Ordinal) ||
            value.Contains("CONNECTION", StringComparison.Ordinal) ||
            value.Contains("503", StringComparison.Ordinal) ||
            value.Contains("502", StringComparison.Ordinal)) return JobFailureKind.RetryableNetwork;
        if (value.Contains("SESSION", StringComparison.Ordinal) ||
            value.Contains("AUTH_KEY", StringComparison.Ordinal) ||
            value.Contains("PHONE_CODE_EXPIRED", StringComparison.Ordinal)) return JobFailureKind.SessionExpired;
        if (value.Contains("CONSENT", StringComparison.Ordinal) || value.Contains("OPT_OUT", StringComparison.Ordinal)) return JobFailureKind.ConsentDenied;
        if (value.Contains("PERMISSION", StringComparison.Ordinal) || value.Contains("CHAT_ADMIN_REQUIRED", StringComparison.Ordinal)) return JobFailureKind.PermissionDenied;
        return JobFailureKind.Permanent;
    }
}

