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
        Assert.Throws<InvalidDataException>(() => loader.Load(directory.Path, "00"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
