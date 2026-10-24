using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class MediaRepositoryTests
{
    [Fact]
    public async Task FindsActiveMediaByHashWithinAccountOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tg-media-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var accountId = await new AccountRepository(path).CreateAsync("Media", "+66000000015");
            var otherAccountId = await new AccountRepository(path).CreateAsync("Other", "+66000000016");
            var repository = new MediaRepository(path);
            var mediaId = await repository.CreateAsync(new MediaFile(0, accountId, "photo.png", "C:\\data\\photo.png", "ABC123", "image/png", 12, DateTimeOffset.UtcNow, null));
            await repository.CreateAsync(new MediaFile(0, otherAccountId, "other.png", "C:\\data\\other.png", "ABC123", "image/png", 13, DateTimeOffset.UtcNow, null));

            var found = await repository.FindBySha256Async(accountId, "ABC123");
            Assert.NotNull(found);
            Assert.Equal(mediaId, found!.Id);
            Assert.Null(await repository.FindBySha256Async(accountId, "missing"));
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}