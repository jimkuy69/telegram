using TelegramControlStudio.Core.Safety;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class SendSafetyPolicyTests
{
    [Fact]
    public void TestModeSkipsRecipientsOutsideAllowlist()
    {
        var settings = new SendSafetySettings(true, new HashSet<long> { 42 }, false, null);

        var decision = SendSafetyPolicy.Evaluate(settings, 99);

        Assert.Equal(SendSafetyVerdict.Skip, decision.Verdict);
        Assert.Equal(SendSafetyPolicy.TestModeReason, decision.ReasonCode);
        Assert.False(decision.CanSend);
    }

    [Fact]
    public void TestModeAllowsAllowlistedRecipient()
    {
        var settings = new SendSafetySettings(true, new HashSet<long> { 42 }, false, null);

        var decision = SendSafetyPolicy.Evaluate(settings, 42);

        Assert.True(decision.CanSend);
        Assert.Equal(SendSafetyPolicy.AllowedReason, decision.ReasonCode);
    }

    [Fact]
    public void LiveModeAllowsAnyConsentedRecipient()
    {
        var settings = new SendSafetySettings(false, new HashSet<long>(), false, null);

        Assert.True(SendSafetyPolicy.Evaluate(settings, 7).CanSend);
    }

    [Fact]
    public void KillSwitchBlocksEvenAllowlistedConsentedRecipient()
    {
        var settings = new SendSafetySettings(false, new HashSet<long> { 42 }, true, null);

        var decision = SendSafetyPolicy.Evaluate(settings, 42, hasConsent: true);

        Assert.Equal(SendSafetyVerdict.Block, decision.Verdict);
        Assert.Equal(SendSafetyPolicy.KillSwitchReason, decision.ReasonCode);
    }

    [Fact]
    public void KillSwitchTakesPrecedenceOverConsent()
    {
        var settings = new SendSafetySettings(true, new HashSet<long>(), true, null);

        var decision = SendSafetyPolicy.Evaluate(settings, 42, hasConsent: false);

        Assert.Equal(SendSafetyVerdict.Block, decision.Verdict);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MissingConsentOrOptOutSkips(bool hasConsent, bool isOptedOut)
    {
        var settings = new SendSafetySettings(false, new HashSet<long>(), false, null);

        var decision = SendSafetyPolicy.Evaluate(settings, 42, hasConsent, isOptedOut);

        Assert.Equal(SendSafetyVerdict.Skip, decision.Verdict);
        Assert.Equal(SendSafetyPolicy.ConsentReason, decision.ReasonCode);
    }

    [Fact]
    public void ParseAllowlistAcceptsCommonSeparatorsAndIgnoresInvalidEntries()
    {
        var allowlist = SendSafetyPolicy.ParseAllowlist("42, 43;44\n45|-1 abc\n 46 ");

        Assert.Equal(new long[] { 42, 43, 44, 45, 46 }, allowlist.Order().ToArray());
    }

    [Fact]
    public void ParseAllowlistReturnsEmptyForBlankInput()
    {
        Assert.Empty(SendSafetyPolicy.ParseAllowlist(null));
        Assert.Empty(SendSafetyPolicy.ParseAllowlist("  "));
    }

    [Fact]
    public void FormatAllowlistOrdersDeduplicatesAndDropsInvalidIds()
    {
        var text = SendSafetyPolicy.FormatAllowlist([45, 42, 45, -3, 0]);

        Assert.Equal($"42{Environment.NewLine}45", text);
    }

    [Fact]
    public void DescribeReflectsKillSwitchAndTestMode()
    {
        var settings = new SendSafetySettings(true, new HashSet<long> { 1, 2 }, false, null);

        Assert.Contains("โหมดทดสอบ", SendSafetyPolicy.Describe(settings, 2));
        Assert.Contains("หยุดฉุกเฉิน", SendSafetyPolicy.Describe(settings with { KillSwitchEngaged = true }, 2));
        Assert.Contains("โหมดใช้งานจริง", SendSafetyPolicy.Describe(settings with { TestModeEnabled = false }, 2));
    }
}
