using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Core.Safety;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class JobTargetTypesTests
{
    [Theory]
    [InlineData("ConsentMessaging", false)]
    [InlineData("Campaign:1b4e", false)]
    [InlineData("ConsentRequest", true)]
    [InlineData("ConsentRequest:12", true)]
    [InlineData("consentrequest", false)]
    [InlineData(null, false)]
    public void RecognisesOnlyConsentRequestJobs(string? targetType, bool expected) =>
        Assert.Equal(expected, JobTargetTypes.IsConsentRequest(targetType));

    [Fact]
    public void MapsEveryOtherJobToTheCampaignGate()
    {
        Assert.Equal(SendPurpose.Campaign, JobTargetTypes.PurposeOf("ConsentMessaging"));
        Assert.Equal(SendPurpose.ConsentRequest, JobTargetTypes.PurposeOf("ConsentRequest"));
    }

    [Fact]
    public void RoundTripsTheTemplateThatProducedTheText()
    {
        Assert.Equal("ConsentRequest:12", JobTargetTypes.ConsentRequestTargetType(12));
        Assert.Equal(12, JobTargetTypes.TemplateIdOf("ConsentRequest:12"));
        Assert.Null(JobTargetTypes.TemplateIdOf("ConsentRequest"));
        Assert.Null(JobTargetTypes.TemplateIdOf("ConsentRequest:abc"));
        Assert.Null(JobTargetTypes.TemplateIdOf("ConsentRequest:0"));
        Assert.Null(JobTargetTypes.TemplateIdOf("ConsentMessaging"));
    }

    [Fact]
    public void IgnoresNonPositiveTemplateIdsWhenBuildingTheTargetType()
    {
        Assert.Equal("ConsentRequest", JobTargetTypes.ConsentRequestTargetType(null));
        Assert.Equal("ConsentRequest", JobTargetTypes.ConsentRequestTargetType(0));
        Assert.Equal("ConsentRequest", JobTargetTypes.ConsentRequestTargetType(-3));
    }
}
