namespace TelegramControlStudio.Core.Reporting;

public static class ResultReportMath
{
    public const string UnspecifiedReason = "UNSPECIFIED";

    public static double Rate(int part, int total) => total <= 0 ? 0 : (double)part / total;

    public static ItemOutcome OutcomeOf(string? status) => status switch
    {
        "Completed" => ItemOutcome.Sent,
        "Failed" => ItemOutcome.Failed,
        "Skipped" => ItemOutcome.Skipped,
        "Cancelled" => ItemOutcome.Cancelled,
        _ => ItemOutcome.Pending,
    };

    public static string NormalizeReason(string? lastError) =>
        string.IsNullOrWhiteSpace(lastError) ? UnspecifiedReason : lastError.Trim();
}

public enum ItemOutcome
{
    Sent,
    Failed,
    Skipped,
    Cancelled,
    Pending
}
