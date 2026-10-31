namespace TelegramControlStudio.Core.Reporting;

/// <summary>
/// One recipient-level outcome as the runners stored it. <c>SentAt</c> is only present once a send
/// actually succeeded, so every other status is attributed to the day its status last changed.
/// </summary>
public sealed record ResultReportItem(
    string Status,
    DateTimeOffset? SentAt,
    DateTimeOffset UpdatedAt,
    string? LastError,
    bool IsConsentRequest);

public sealed record ResultReportDay(
    DateOnly Day,
    int Sent,
    int Failed,
    int Skipped,
    int Cancelled,
    int Pending,
    int ConsentRequestsSent)
{
    public int Settled => Sent + Failed + Skipped;
    public double SuccessRate => ResultReportMath.Rate(Sent, Settled);
}

public sealed record ResultReportFailure(string Reason, int Count);

public sealed record ResultReportConsent(int Requested, int Granted, int Rejected, int OptedOut)
{
    public int AwaitingAnswer => Math.Max(0, Requested - Granted - Rejected - OptedOut);
    public double GrantRate => ResultReportMath.Rate(Granted, Granted + Rejected + OptedOut);
}

/// <summary>Aggregate result of one account's sending activity over a date range.</summary>
public sealed record ResultReport(
    long AccountId,
    DateTimeOffset From,
    DateTimeOffset To,
    int CampaignJobs,
    int ConsentJobs,
    int TotalItems,
    int Sent,
    int Failed,
    int Skipped,
    int Cancelled,
    int Pending,
    int ConsentRequestsSent,
    IReadOnlyList<ResultReportDay> Days,
    IReadOnlyList<ResultReportFailure> Failures,
    ResultReportConsent Consent)
{
    public int Settled => Sent + Failed + Skipped;
    public double SuccessRate => ResultReportMath.Rate(Sent, Settled);
}
