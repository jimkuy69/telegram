using TelegramControlStudio.Core.Operations;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ContactConsentPolicyTests
{
    [Theory]
    [InlineData(true, false, ContactConsentState.Consented)]
    [InlineData(false, false, ContactConsentState.NotAllowed)]
    [InlineData(true, true, ContactConsentState.OptedOut)]
    [InlineData(false, true, ContactConsentState.OptedOut)]
    public void ClassifyOptOutWinsOverConsent(bool hasConsent, bool isOptedOut, ContactConsentState expected)
    {
        Assert.Equal(expected, ContactConsentPolicy.Classify(hasConsent, isOptedOut));
    }

    [Theory]
    [InlineData(ContactConsentState.Consented, ContactConsentPolicy.ConsentedLabel)]
    [InlineData(ContactConsentState.NotAllowed, ContactConsentPolicy.NotAllowedLabel)]
    [InlineData(ContactConsentState.OptedOut, ContactConsentPolicy.OptedOutLabel)]
    public void LabelMatchesDisplayText(ContactConsentState state, string expected)
    {
        Assert.Equal(expected, ContactConsentPolicy.Label(state));
    }

    [Fact]
    public void SortRankOrdersConsentedThenNotAllowedThenOptedOut()
    {
        Assert.True(ContactConsentPolicy.SortRank(ContactConsentState.Consented) < ContactConsentPolicy.SortRank(ContactConsentState.NotAllowed));
        Assert.True(ContactConsentPolicy.SortRank(ContactConsentState.NotAllowed) < ContactConsentPolicy.SortRank(ContactConsentState.OptedOut));
    }
}
