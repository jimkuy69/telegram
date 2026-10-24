using TelegramControlStudio.Infrastructure.Security;
using System.Runtime.Versioning;
using Xunit;

namespace TelegramControlStudio.Tests;

[SupportedOSPlatform("windows")]
public sealed class EncryptedBackupServiceTests
{
    [Fact]
    public async Task BackupAndRestoreRoundTripPreservesBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tcs-backup-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        var database = Path.Combine(root, "source.db"); var backup = Path.Combine(root, "backup.tcs"); var restored = Path.Combine(root, "restored.db");
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("local sqlite test payload"); await File.WriteAllBytesAsync(database, bytes);
            var service = new EncryptedBackupService(); await service.BackupAsync(database, backup); await service.RestoreAsync(backup, restored);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(restored)); Assert.NotEqual(Convert.ToBase64String(bytes), Convert.ToBase64String(await File.ReadAllBytesAsync(backup)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InvalidBackupDoesNotReplaceExistingDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tcs-backup-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        var database = Path.Combine(root, "database.db"); var backup = Path.Combine(root, "invalid.tcs");
        try
        {
            var original = System.Text.Encoding.UTF8.GetBytes("keep this database");
            await File.WriteAllBytesAsync(database, original);
            await File.WriteAllBytesAsync(backup, System.Text.Encoding.UTF8.GetBytes("not encrypted"));
            await Assert.ThrowsAsync<InvalidDataException>(() => new EncryptedBackupService().RestoreAsync(backup, database));
            Assert.Equal(original, await File.ReadAllBytesAsync(database));
            Assert.False(File.Exists(database + ".restore.tmp"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

