using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class AccountTurnPolicyTests
{
    [Fact]
    public void RotationStartsWithTheLowestAccountWhenNothingWasServedYet()
        => Assert.Equal(new[] { 1L, 2L, 3L }, AccountTurnPolicy.RotateForFairness(new[] { 3L, 1L, 2L }, null));

    [Fact]
    public void RotationContinuesAfterTheAccountThatWasServedLast()
        => Assert.Equal(new[] { 3L, 1L, 2L }, AccountTurnPolicy.RotateForFairness(new[] { 1L, 2L, 3L }, 2));

    [Fact]
    public void RotationIgnoresAnAccountThatIsNoLongerConnected()
        => Assert.Equal(new[] { 1L, 2L }, AccountTurnPolicy.RotateForFairness(new[] { 1L, 2L }, 99));

    [Fact]
    public void RotationDeduplicatesAndSurvivesAnEmptyRegistry()
    {
        Assert.Equal(new[] { 7L }, AccountTurnPolicy.RotateForFairness(new[] { 7L, 7L }, null));
        Assert.Empty(AccountTurnPolicy.RotateForFairness(Array.Empty<long>(), null));
    }

    [Fact]
    public void SelectForPassTakesTheOldestEligibleJobAndSkipsBlockedOnes()
    {
        var now = DateTimeOffset.UtcNow;
        var blocked = Job(now.AddMinutes(-9), JobStatus.Paused, "ข้อความ");
        var oldest = Job(now.AddMinutes(-8), JobStatus.Queued, "ข้อความ");
        var newest = Job(now.AddMinutes(-1), JobStatus.Queued, "ข้อความ");
        var blank = Job(now.AddMinutes(-2), JobStatus.Queued, "   ");

        var selected = AccountTurnPolicy.SelectForPass([blocked, newest, oldest, blank], now);

        Assert.Single(selected);
        Assert.Equal(oldest.Id, selected[0].Id);
    }

    [Fact]
    public void SelectForPassHonoursTheRequestedLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var jobs = Enumerable.Range(1, 5).Select(offset => Job(now.AddMinutes(-offset), JobStatus.Queued, "ข้อความ")).ToList();

        Assert.Equal(3, AccountTurnPolicy.SelectForPass(jobs, now, limit: 3).Count);
        Assert.Empty(AccountTurnPolicy.SelectForPass(jobs, now, limit: 0));
    }

    [Fact]
    public void SelectForPassResumesAFloodWaitedJobOnceTheCooldownElapsed()
    {
        var now = DateTimeOffset.UtcNow;
        var waiting = Job(now.AddMinutes(-5), JobStatus.WaitingForFloodWait, "ข้อความ")
            with { FloodWaitUntil = now.AddSeconds(-1) };
        var cooling = Job(now.AddMinutes(-4), JobStatus.WaitingForFloodWait, "ข้อความ")
            with { FloodWaitUntil = now.AddMinutes(2) };

        var selected = AccountTurnPolicy.SelectForPass([cooling, waiting], now);

        Assert.Equal(waiting.Id, Assert.Single(selected).Id);
    }

    private static Job Job(DateTimeOffset createdAt, JobStatus status, string messageText) =>
        new(Guid.NewGuid(), 1, "ConsentMessaging", 1, status, 0, createdAt, null, null, null, null, $"key-{Guid.NewGuid():N}", messageText);
}
