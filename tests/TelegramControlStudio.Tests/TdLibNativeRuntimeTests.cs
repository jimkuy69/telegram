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
        var root = FindRepositoryRoot();
        var nativeDirectory = Path.Combine(root, "tdlib");
        var manifestPath = Path.Combine(nativeDirectory, "tdjson.manifest.json");
        Assert.True(File.Exists(manifestPath), "Verified TDLib manifest is not present in this checkout.");
        Assert.True(File.Exists(Path.Combine(nativeDirectory, "tdjson.dll")), "Verified TDLib binary is not present in this checkout.");

        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var expectedHash = manifestDocument.RootElement.GetProperty("sha256").GetString();
        Assert.False(string.IsNullOrWhiteSpace(expectedHash));

        var loader = new TdLibNativeLoader();
        var handle = loader.Load(nativeDirectory, expectedHash!);
        using var client = new TdLibNativeClient(handle);

        var clientId = client.CreateClientId();
        Assert.True(clientId > 0);

        var response = client.Execute("{\"@type\":\"getOption\",\"name\":\"version\"}");
        Assert.False(string.IsNullOrWhiteSpace(response));
        using var responseDocument = JsonDocument.Parse(response!);
        Assert.True(responseDocument.RootElement.TryGetProperty("@type", out _), $"Unexpected TDLib response: {response}");
        Assert.Equal("optionValueString", responseDocument.RootElement.GetProperty("@type").GetString());
        var value = responseDocument.RootElement.GetProperty("value").GetString();
        Assert.False(string.IsNullOrWhiteSpace(value));
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