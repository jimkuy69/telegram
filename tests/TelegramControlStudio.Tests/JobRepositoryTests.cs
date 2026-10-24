using TelegramControlStudio.Infrastructure.Persistence;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class JobRepositoryTests
{
    [Fact]
    public async Task RequiresPositiveAccountId()
    {
        var repository = new JobRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.CreateAsync(0, "contacts", 1, "idempotency-key"));
    }
}
