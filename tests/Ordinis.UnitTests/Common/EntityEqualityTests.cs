using Ordinis.Domain.Organizations;

namespace Ordinis.UnitTests.Common;

/// <summary>
/// Verifies the identity-based equality contract defined in <see cref="Entity"/>.
/// Uses <see cref="FakeEntity"/> as a concrete subclass — the behaviour under
/// test is inherited and identical across all entities.
/// </summary>
public sealed class EntityEqualityTests
{
    [Fact]
    public void Equals_SameId_ReturnsTrue()
    {
        var id = Guid.CreateVersion7();
        var a = new FakeEntity(id);
        var b = new FakeEntity(id);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_DifferentId_ReturnsFalse()
    {
        var a = new FakeEntity(Guid.CreateVersion7());
        var b = new FakeEntity(Guid.CreateVersion7());

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EqualityOperator_SameId_ReturnsTrue()
    {
        var id = Guid.CreateVersion7();
        var a = new FakeEntity(id);
        var b = new FakeEntity(id);

        Assert.True(a == b);
    }

    [Fact]
    public void GetHashCode_SameId_ReturnsSameHash()
    {
        var id = Guid.CreateVersion7();
        var a = new FakeEntity(id);
        var b = new FakeEntity(id);

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
