using Microsoft.EntityFrameworkCore;

namespace Ordinis.UnitTests.Common;

/// <summary>
/// Creates <see cref="TestAppDbContext"/> instances for unit tests, each backed by a
/// uniquely-named EF Core InMemory database so no state leaks between tests.
/// </summary>
internal static class TestDbContextFactory
{
    public static TestAppDbContext Create()
    {
        DbContextOptions<TestAppDbContext> options = new DbContextOptionsBuilder<TestAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestAppDbContext(options);
    }
}
