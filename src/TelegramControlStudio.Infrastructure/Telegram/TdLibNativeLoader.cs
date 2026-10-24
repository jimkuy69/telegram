using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibNativeLoader
{
    public nint Load(string nativeDirectory, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
            throw new ArgumentException("A trusted SHA-256 hash is required.", nameof(expectedSha256));

        var directory = Path.GetFullPath(nativeDirectory);
        var path = Path.Combine(directory, "tdjson.dll");
        if (!File.Exists(path))
            throw new FileNotFoundException("The TDLib native dependency was not found.", path);

        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The native dependency path escaped its allowed directory.");

        var actualSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath)));
        if (!actualSha256.Equals(expectedSha256.Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("TDLib native dependency hash mismatch.");

        return NativeLibrary.Load(fullPath);
    }
}
