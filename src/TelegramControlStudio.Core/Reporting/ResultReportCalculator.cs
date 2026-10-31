namespace TelegramControlStudio.Core.Reporting;

/// <summary>
/// Turns recipient-level rows into the aggregate report. All arithmetic lives here so the
/// repository stays a pure query and the numbers can be tested without a database.
/// </summary>
public static class ResultReportCalculator
{
    public static ResultReport Build(
        long accountId,
        DateTimeOffset from,
        DateTimeOffset to,
        int campaignJobs,
        int consentJobs,
        IEnumerable<ResultReportItem> items,
        ResultReportConsent consent,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(consent);
        ArgumentNullException.ThrowIfNull(zone);
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        if (from > to) throw new ArgumentException("Start must not be after end.", nameof(from));

        var days = new Dictionary<DateOnly, Counters>();
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = new Counters();

        foreach (var item in items)
        {
            var outcome = ResultReportMath.OutcomeOf(item.Status);
            total.Add(outcome, item.IsConsentRequest);

            var occurredAt = (item.SentAt ?? item.UpdatedAt).ToUniversalTime();
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(occurredAt, zone).DateTime);
            var counters = GetOrCreate(days, day);
            counters.Add(outcome, item.IsConsentRequest);

            if (outcome is ItemOutcome.Failed or ItemOutcome.Skipped or ItemOutcome.Cancelled)
            {
                var reason = ResultReportMath.NormalizeReason(item.LastError);
                failures[reason] = failures.GetValueOrDefault(reason) + 1;
            }
        }

        var rows = days
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value.ToRow(pair.Key))
            .ToList();

        var failureRows = failures
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ResultReportFailure(pair.Key, pair.Value))
            .ToList();

        return new ResultReport(
            accountId,
            from,
            to,
            campaignJobs,
            consentJobs,
            total.All,
            total.Sent,
            total.Failed,
            total.Skipped,
            total.Cancelled,
            total.Pending,
            total.ConsentRequestsSent,
            rows,
            failureRows,
            consent);
    }

    private static Counters GetOrCreate(Dictionary<DateOnly, Counters> days, DateOnly day)
    {
        if (!days.TryGetValue(day, out var counters))
        {
            counters = new Counters();
            days[day] = counters;
        }
        return counters;
    }

    private sealed class Counters
    {
        public int Sent;
        public int Failed;
        public int Skipped;
        public int Cancelled;
        public int Pending;
        public int ConsentRequestsSent;
        public int All => Sent + Failed + Skipped + Cancelled + Pending;

        public void Add(ItemOutcome outcome, bool isConsentRequest)
        {
            switch (outcome)
            {
                case ItemOutcome.Sent: Sent++; break;
                case ItemOutcome.Failed: Failed++; break;
                case ItemOutcome.Skipped: Skipped++; break;
                case ItemOutcome.Cancelled: Cancelled++; break;
                default: Pending++; break;
            }
            if (outcome == ItemOutcome.Sent && isConsentRequest) ConsentRequestsSent++;
        }

        public ResultReportDay ToRow(DateOnly day) => new(day, Sent, Failed, Skipped, Cancelled, Pending, ConsentRequestsSent);
    }
}
