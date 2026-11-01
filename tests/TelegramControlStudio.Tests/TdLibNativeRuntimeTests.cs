using System.Text.Json;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

[Trait("Category", "NativeRuntime")]
public sealed class TdLibNativeRuntimeTests
{
    [Fact]
    public void LoadsVerifiedNativeLibraryAndExecutesSynchronousOptionRequest()
    {
        if (!IsRuntimeGateEnabled())
        {
            return;
        }

        using var client = CreateVerifiedClient();

        var response = client.Execute("{\"@type\":\"getOption\",\"name\":\"version\"}");
        Assert.False(string.IsNullOrWhiteSpace(response));
        using var responseDocument = JsonDocument.Parse(response!);
        Assert.True(responseDocument.RootElement.TryGetProperty("@type", out _), $"Unexpected TDLib response: {response}");
        Assert.Equal("optionValueString", responseDocument.RootElement.GetProperty("@type").GetString());
        var value = responseDocument.RootElement.GetProperty("value").GetString();
        Assert.False(string.IsNullOrWhiteSpace(value));
    }

    /// <summary>
    /// TDLib aborts the whole process when its process-global receive entry point is reached from
    /// more than one thread, which used to close the app on the second account connect. Every
    /// client must be sent to and received from independently.
    /// </summary>
    [Fact]
    public async Task TwoClientsReceiveOnSeparateDedicatedThreads()
    {
        if (!IsRuntimeGateEnabled())
        {
            return;
        }

        using var firstClient = CreateVerifiedClient();
        using var secondClient = CreateVerifiedClient();

        // Both threads are joined before the clients are disposed: TDLib must not have a receive
        // call in flight when the instance is destroyed.
        var first = StartDedicatedRequest(firstClient, "two-clients-first");
        var second = StartDedicatedRequest(secondClient, "two-clients-second");
        Assert.True(first.thread.Join(TimeSpan.FromSeconds(35)), "The first receive thread did not stop.");
        Assert.True(second.thread.Join(TimeSpan.FromSeconds(35)), "The second receive thread did not stop.");

        var firstResponse = await first.completion.Task;
        var secondResponse = await second.completion.Task;
        Assert.Contains("authorizationState", firstResponse, StringComparison.Ordinal);
        Assert.Contains("authorizationState", secondResponse, StringComparison.Ordinal);
    }

    private static (Thread thread, TaskCompletionSource<string> completion) StartDedicatedRequest(
        TdLibNativeClient client,
        string extra)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                client.Send($$"""{"@type":"getAuthorizationState","@extra":"{{extra}}"}""");
                for (var attempt = 0; attempt < 60; attempt++)
                {
                    var json = client.Receive(0.5);
                    if (json is not null && json.Contains(extra, StringComparison.Ordinal))
                    {
                        completion.TrySetResult(json);
                        return;
                    }
                }
                completion.TrySetResult(string.Empty);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        { IsBackground = true };

        thread.Start();
        return (thread, completion);
    }

    private static bool IsRuntimeGateEnabled()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("TDLIB_RUNTIME_GATE"), "1", StringComparison.Ordinal))
        {
            return true;
        }

        Console.WriteLine("SKIPPED: TDLib native runtime requires the dedicated runtime gate.");
        return false;
    }

    private static TdLibNativeClient CreateVerifiedClient()
    {
        var root = FindRepositoryRoot();
        var nativeDirectory = Path.Combine(root, "tdlib");
        var manifestPath = Path.Combine(nativeDirectory, "tdjson.manifest.json");
        Assert.True(File.Exists(manifestPath), "Verified TDLib manifest is not present in this checkout.");
        Assert.True(File.Exists(Path.Combine(nativeDirectory, "tdjson.dll")), "Verified TDLib binary is not present in this checkout.");

        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var expectedHash = manifestDocument.RootElement.GetProperty("sha256").GetString();
        Assert.False(string.IsNullOrWhiteSpace(expectedHash));

        return new TdLibNativeClient(new TdLibNativeLoader().Load(nativeDirectory, expectedHash!));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TelegramControlStudio.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
