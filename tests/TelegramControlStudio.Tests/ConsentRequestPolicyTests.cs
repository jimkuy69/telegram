using TelegramControlStudio.Core.Safety;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ConsentRequestPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static ConsentRequestEligibility Eligibility(
        bool hasOwnNumber = true,
        bool alreadyConsented = false,
        bool isOptedOut = false,
        DateTimeOffset? lastRequestedAt = null,
        int requestedToday = 0,
        int cooldownDays = ConsentRequestPolicy.DefaultCooldownDays,
        int dailyLimit = ConsentRequestPolicy.DefaultDailyLimit) =>
        new(42, hasOwnNumber, alreadyConsented, isOptedOut, lastRequestedAt, requestedToday, cooldownDays, dailyLimit, Now);

    [Fact]
    public void FreshRequestToAContactWhoseNumberWeHoldIsAllowed()
        => Assert.Equal(ConsentRequestPolicy.AllowedReason, ConsentRequestPolicy.Evaluate(Eligibility()).ReasonCode);

    [Fact]
    public void ContactWithoutAStoredNumberIsNeverAsked()
        => Assert.Equal(ConsentRequestPolicy.NoOwnNumberReason, ConsentRequestPolicy.Evaluate(Eligibility(hasOwnNumber: false)).ReasonCode);

    [Fact]
    public void AlreadyConsentedContactDoesNotNeedARequest()
        => Assert.Equal(ConsentRequestPolicy.AlreadyConsentedReason, ConsentRequestPolicy.Evaluate(Eligibility(alreadyConsented: true)).ReasonCode);

    [Fact]
    public void OptedOutContactIsSkippedEvenWhenItWasNeverRequested()
        => Assert.Equal(ConsentRequestPolicy.OptedOutReason, ConsentRequestPolicy.Evaluate(Eligibility(isOptedOut: true)).ReasonCode);

    [Fact]
    public void OptOutOutranksEveryOtherState()
    {
        var decision = ConsentRequestPolicy.Evaluate(Eligibility(alreadyConsented: true, isOptedOut: true));
        Assert.Equal(ConsentRequestPolicy.OptedOutReason, decision.ReasonCode);
    }

    [Fact]
    public void ARequestInsideTheCooldownIsSkipped()
        => Assert.Equal(ConsentRequestPolicy.CooldownReason, ConsentRequestPolicy.Evaluate(Eligibility(lastRequestedAt: Now.AddDays(-10))).ReasonCode);

    [Fact]
    public void ARequestOlderThanTheCooldownMayBeRepeated()
        => Assert.Equal(ConsentRequestPolicy.AllowedReason, ConsentRequestPolicy.Evaluate(Eligibility(lastRequestedAt: Now.AddDays(-31))).ReasonCode);

    [Fact]
    public void ReachingTheDailyLimitStopsFurtherRequests()
        => Assert.Equal(ConsentRequestPolicy.DailyLimitReason, ConsentRequestPolicy.Evaluate(Eligibility(requestedToday: 20)).ReasonCode);

    [Fact]
    public void AZeroDailyLimitIsTreatedAsSwitchedOffRatherThanUnlimited()
        => Assert.Equal(ConsentRequestPolicy.DailyLimitReason, ConsentRequestPolicy.Evaluate(Eligibility(dailyLimit: 0)).ReasonCode);

    [Fact]
    public void KillSwitchBlocksARequestJustLikeACampaignSend()
    {
        var settings = new SendSafetySettings(true, new HashSet<long> { 42 }, true, Now);
        var decision = SendSafetyPolicy.EvaluateConsentRequest(settings, Eligibility());
        Assert.Equal(SendSafetyVerdict.Block, decision.Verdict);
        Assert.Equal(SendSafetyPolicy.KillSwitchReason, decision.ReasonCode);
    }

    [Fact]
    public void TestModeStillLimitsRequestsToTheAllowlist()
    {
        var settings = new SendSafetySettings(true, new HashSet<long>(), false, Now);
        var decision = SendSafetyPolicy.EvaluateConsentRequest(settings, Eligibility());
        Assert.Equal(SendSafetyPolicy.TestModeReason, decision.ReasonCode);
    }

    [Fact]
    public void RequestPolicyDoesNotReuseTheCampaignConsentRequirement()
    {
        var settings = new SendSafetySettings(false, new HashSet<long>(), false, Now);
        Assert.Equal(SendSafetyPolicy.ConsentReason, SendSafetyPolicy.Evaluate(settings, 42, hasConsent: false).ReasonCode);
        Assert.True(SendSafetyPolicy.EvaluateConsentRequest(settings, Eligibility()).CanSend);
    }

    [Fact]
    public void CampaignRequestsStillRefuseAnUnconsentedRecipientAtTheRequestBuilder()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => TelegramControlStudio.Core.Telegram.TdLibMessageRequests.SendText(7, "hello", hasConsent: false, isOptedOut: false, confirmed: true));
        Assert.Equal("CONSENT_REQUIRED", exception.Message);
    }

    [Fact]
    public void ConsentRequestPurposeIsWhatOpensTheRequestBuilder()
    {
        var request = TelegramControlStudio.Core.Telegram.TdLibMessageRequests.SendText(
            7, "may I message you?", hasConsent: false, isOptedOut: false, confirmed: true, purpose: SendPurpose.ConsentRequest);
        Assert.Equal("sendMessage", request.Function);
        Assert.Equal(7L, request.Parameters["chat_id"]);
    }
}
