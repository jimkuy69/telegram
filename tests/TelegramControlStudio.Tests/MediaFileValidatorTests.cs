using TelegramControlStudio.Core.Import;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class MediaFileValidatorTests
{
    [Fact]
    public void AcceptsAllowedFileInsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-root");
        var result = MediaFileValidator.Validate(Path.Combine(root, "photo.png"), "image/png", 100, root);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void RejectsPathTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-root");
        var result = MediaFileValidator.Validate(Path.Combine(root, "..", "secret.txt"), "image/png", 100, root);
        Assert.False(result.IsValid);
        Assert.Equal("PATH_OUTSIDE_ALLOWED_ROOT", result.ErrorCode);
    }

    [Fact]
    public void RejectsSiblingDirectoryWithSharedNamePrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-root");
        var sibling = Path.Combine(Path.GetTempPath(), "media-root-archive", "photo.png");
        var result = MediaFileValidator.Validate(sibling, "image/png", 100, root);
        Assert.False(result.IsValid);
        Assert.Equal("PATH_OUTSIDE_ALLOWED_ROOT", result.ErrorCode);
    }
}

