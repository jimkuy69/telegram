using Xunit;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.Tests;

public sealed class TdLibNativeLoaderTests
{
    [Fact]
    public void RejectsMissingTrustedHash()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "tdjson.dll"), "not-a-native-library");
        var loader = new TdLibNativeLoader();
        Assert.Throws<ArgumentException>(() => loader.Load(directory.Path, string.Empty));
    }

    [Fact]
    public void RejectsHashMismatchBeforeLoading()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "tdjson.dll"), "not-a-native-library");
        var loader = new TdLibNativeLoader();
        Assert.Throws<InvalidDataException>(() => loader.Load(directory.Path, new string('0', 64)));
    }

    [Theory]
    [InlineData("00")]
    [InlineData("not-a-hash")]
    public void RejectsMalformedTrustedHash(string expectedHash)
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "tdjson.dll"), "not-a-native-library");
        Assert.Throws<ArgumentException>(() => new TdLibNativeLoader().Load(directory.Path, expectedHash));
    }

    [Fact]
    public void ComputesStableDependencyHashForReleaseManifest()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "tdjson.dll"), "tdlib-test-binary");
        var loader = new TdLibNativeLoader();
        var hash = loader.ComputeSha256(directory.Path);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, loader.ComputeSha256(directory.Path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

