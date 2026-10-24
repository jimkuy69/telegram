using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace TelegramControlStudio.Infrastructure.Security;

[SupportedOSPlatform("windows")]
public sealed class EncryptedBackupService
{
    private static readonly byte[] Header = Encoding.UTF8.GetBytes("TCS-BACKUP-V1\n");
    private readonly DpapiSecretStore _store = new();

    public async Task BackupAsync(string databasePath, string backupPath, CancellationToken cancellationToken = default)
    {
        ValidatePaths(databasePath, backupPath);
        var database = await File.ReadAllBytesAsync(databasePath, cancellationToken);
        var protectedBytes = _store.Protect(Convert.ToBase64String(database));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(backupPath))!);
        await using var stream = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(Header, cancellationToken); await stream.WriteAsync(protectedBytes, cancellationToken);
    }

    public async Task RestoreAsync(string backupPath, string databasePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupPath)) throw new FileNotFoundException("Encrypted backup was not found.", backupPath);
        if (string.Equals(Path.GetFullPath(backupPath), Path.GetFullPath(databasePath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Backup path must differ from database path.", nameof(databasePath));
        var payload = await File.ReadAllBytesAsync(backupPath, cancellationToken);
        if (payload.Length <= Header.Length || !payload.AsSpan(0, Header.Length).SequenceEqual(Header)) throw new InvalidDataException("Backup header is invalid.");
        var clear = _store.Unprotect(payload[Header.Length..]);
        var database = Convert.FromBase64String(clear);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var temporaryPath = databasePath + ".restore.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, database, cancellationToken);
            File.Move(temporaryPath, databasePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void ValidatePaths(string databasePath, string backupPath)
    {
        if (!File.Exists(databasePath)) throw new FileNotFoundException("Database was not found.", databasePath);
        if (string.Equals(Path.GetFullPath(databasePath), Path.GetFullPath(backupPath), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Backup path must differ from database path.");
        if (File.Exists(backupPath)) throw new IOException("Backup target already exists.");
    }
}

