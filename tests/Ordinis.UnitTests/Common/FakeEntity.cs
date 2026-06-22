using Ordinis.Domain.Common;

namespace Ordinis.UnitTests.Common;

/// <summary>
/// Minimal concrete subclass of <see cref="Entity"/> used exclusively to test
/// the identity-based equality contract. No production code is involved.
/// </summary>
public class FakeEntity : Entity
{
    /// <summary>
    /// Creates a new entity with a generated Id.
    /// </summary>
    public FakeEntity() { }

    /// <summary>
    /// Creates an entity with a known Id for equality assertion.
    /// </summary>
    /// <param name="id"></param>
    public FakeEntity(Guid id) : base(id) { }
}
