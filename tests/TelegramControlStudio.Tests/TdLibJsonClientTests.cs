using Xunit;
using System.Text.Json;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.Tests;

public sealed class TdLibJsonClientTests
{
    [Fact]
    public async Task CorrelatesResponseUsingExtra()
    {
        using var writer = new StringWriter();
        await using var client = new TdLibJsonClient(writer);
        var pending = client.SendAsync("getOption", new Dictionary<string, object?> { ["name"] = "version" });
        await Task.Delay(10);
        using var request = JsonDocument.Parse(writer.ToString());
        var extra = request.RootElement.GetProperty("@extra").GetString();
        Assert.True(client.HandleResponse($$"""{"@type":"optionValueString","value":"test","@extra":"{{extra}}"}"""));
        Assert.Contains("optionValueString", await pending);
        Assert.Equal(0, client.PendingRequestCount);
    }

    [Fact]
    public async Task CancellationRemovesPendingRequest()
    {
        using var writer = new StringWriter();
        await using var client = new TdLibJsonClient(writer);
        using var cancellation = new CancellationTokenSource();
        var pending = client.SendAsync("getOption", new Dictionary<string, object?>(), cancellation.Token);
        await Task.Delay(10);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.Equal(0, client.PendingRequestCount);
    }

    [Fact]
    public async Task MalformedResponseIsRejectedWithoutThrowing()
    {
        await using var client = new TdLibJsonClient(new StringWriter());
        Assert.False(client.HandleResponse("not-json"));
        Assert.Equal(0, client.PendingRequestCount);
    }
}

