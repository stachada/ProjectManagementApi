using Ordinis.Domain.Users;

namespace Ordinis.UnitTests.Common.Builders;

/// <summary>
/// Creates valid <see cref="User"/> instances for unit tests.
/// </summary>
internal static class UserBuilder
{
    public static User Create(
        Guid? organizationId = null,
        string displayName = "Test User",
        string email = "test@example.com",
        string passwordHash = "hashed-password",
        Role orgRole = Role.Member) =>
        User.Create(
            organizationId ?? Guid.NewGuid(),
            displayName,
            email,
            passwordHash,
            orgRole);
}
