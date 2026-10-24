using TelegramControlStudio.Core.Operations;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class FloodWaitParserTests
{
    [Fact]
    public void ParsesTelegramFloodWaitSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(42), FloodWaitParser.TryParse("429: FLOOD_WAIT_42"));
    }

    [Fact]
    public void IgnoresUnrelatedErrors()
    {
        Assert.Null(FloodWaitParser.TryParse("PHONE_NUMBER_INVALID"));
    }
}
