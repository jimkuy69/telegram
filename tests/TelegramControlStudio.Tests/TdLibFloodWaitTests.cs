using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibFloodWaitTests
{
    [Fact]
    public void FloodWaitExceptionClampsInvalidDuration()
    {
        var exception = new TdLibFloodWaitException(0);

        Assert.Equal(1, exception.Seconds);
        Assert.Equal("FLOOD_WAIT_0", exception.Message);
    }
}
