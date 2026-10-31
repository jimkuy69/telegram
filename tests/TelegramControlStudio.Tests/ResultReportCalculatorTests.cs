using TelegramControlStudio.Core.Reporting;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ResultReportCalculatorTests
{
    /// <summary>18:00 UTC is already the next calendar day in UTC+7, so day bucketing must follow the local date.</summary>
    private static readonly DateTimeOffset LateUtcEvening = new(2026, 10, 30, 18, 0, 0, TimeSpan.Zero);

    private static TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

    private static ResultReportItem Sent(bool isConsentRequest = false) =>
        new("Completed", LateUtcEvening, LateUtcEvening.AddHours(-1), null, isConsentRequest);

    private static ResultReport Build(IEnumerable<ResultReportItem> items, ResultReportConsent? consent = null) =>
        ResultReportCalculator.Build(1, LateUtcEvening.AddHours(-12), LateUtcEvening.AddHours(12), 3, 1, items,
            consent ?? new ResultReportConsent(0, 0, 0, 0), Zone);

    [Fact]
    public void BucketsRecipientsByTheirLocalDayAndLeavesPendingOutOfTheRate()
    {
        var report = Build(
        [
            Sent(),
            Sent(isConsentRequest: true),
            new("Failed", null, LateUtcEvening, "KILL_SWITCH_ENGAGED", false),
            new("Queued", null, LateUtcEvening, "FLOOD_WAIT_30", false)
        ], new ResultReportConsent(4, 2, 1, 1));

        var day = Assert.Single(report.Days);
        Assert.Equal(new DateOnly(2026, 10, 31), day.Day);
        Assert.Equal(2, day.Sent);
        Assert.Equal(1, day.ConsentRequestsSent);
        Assert.Equal(1, day.Failed);
        Assert.Equal(1, day.Pending);
        Assert.Equal(3, day.Settled);
        Assert.Equal(2d / 3, day.SuccessRate, 6);

        Assert.Equal(4, report.TotalItems);
        Assert.Equal(3, report.Settled);
        Assert.Equal(2d / 3, report.SuccessRate, 6);
        Assert.Equal(1, report.ConsentRequestsSent);

        var failure = Assert.Single(report.Failures);
        Assert.Equal("KILL_SWITCH_ENGAGED", failure.Reason);
        Assert.Equal(1, failure.Count);

        Assert.Equal(0, report.Consent.AwaitingAnswer);
        Assert.Equal(0.5, report.Consent.GrantRate, 6);
    }

    [Fact]
    public void GroupsMissingReasonsUnderAnExplicitCodeAndOrdersByVolume()
    {
        var report = Build(
        [
            new("Failed", null, LateUtcEvening, null, false),
            new("Skipped", null, LateUtcEvening, "  CONTACT_NOT_FOUND  ", false),
            new("Skipped", null, LateUtcEvening, "CONTACT_NOT_FOUND", false),
            new("Cancelled", null, LateUtcEvening, "KILL_SWITCH_ENGAGED", false)
        ]);

        Assert.Equal(new[]
        {
            "CONTACT_NOT_FOUND",
            "KILL_SWITCH_ENGAGED",
            ResultReportMath.UnspecifiedReason
        }, report.Failures.Select(failure => failure.Reason).ToArray());
        Assert.Equal(2, report.Failures[0].Count);
        Assert.Equal(0, report.SuccessRate);
    }

    [Fact]
    public void EmptyWindowProducesZeroedCardsInsteadOfDivideByNothing()
    {
        var report = Build(Array.Empty<ResultReportItem>());

        Assert.Equal(0, report.TotalItems);
        Assert.Empty(report.Days);
        Assert.Empty(report.Failures);
        Assert.Equal(0, report.SuccessRate);
        Assert.Equal(0, report.Consent.GrantRate);
    }

    [Fact]
    public void RejectsAnInvertedWindowOrAMissingAccount()
    {
        Assert.Throws<ArgumentException>(() => ResultReportCalculator.Build(
            1, LateUtcEvening, LateUtcEvening.AddHours(-1), 0, 0, Array.Empty<ResultReportItem>(),
            new ResultReportConsent(0, 0, 0, 0), Zone));
        Assert.Throws<ArgumentOutOfRangeException>(() => ResultReportCalculator.Build(
            0, LateUtcEvening.AddHours(-1), LateUtcEvening, 0, 0, Array.Empty<ResultReportItem>(),
            new ResultReportConsent(0, 0, 0, 0), Zone));
    }
}
