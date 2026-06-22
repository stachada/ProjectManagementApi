using Ordinis.Domain.Common;
using Ordinis.Domain.Organizations;

namespace Ordinis.UnitTests.Organizations;

/// <summary>
/// Verifies <see cref="Organization"/> aggregate invariants:
/// active/suspended state guards, rename/description mutations.
/// </summary>
public sealed class OrganizationTests
{
    #region Create
    [Fact]
    public void Create_ValidArguments_OrganizationIsActive()
    {
        var org = Organization.Create("Acme Corp", "acme-corp");

        Assert.True(org.IsActive);
        Assert.Equal("Acme Corp", org.Name);
        Assert.Equal("acme-corp", org.Slug);
    }
    #endregion

    #region Suspend / Reactivate
    [Fact]
    public void Suspend_ActiveOrganization_SetsIsActiveFalse()
    {
        var org = Organization.Create("Acme Corp", "acme-corp");

        org.Suspend();

        Assert.False(org.IsActive);
    }

    [Fact]
    public void Suspend_AlreadySuspended_ThrowsDomainException()
    {
        var org = Organization.Create("Acme Corp", "acme-corp");
        org.Suspend();

        DomainException ex = Assert.Throws<DomainException>(() => org.Suspend());

        Assert.Equal("organization.already-suspended", ex.ErrorCode);
    }

    [Fact]
    public void Reactivate_SuspendedOrganization_SetsIsActiveTrue()
    {
        var org = Organization.Create("Acme Corp", "acme-corp");
        org.Suspend();

        org.Reactivate();

        Assert.True(org.IsActive);
    }

    [Fact]
    public void Reactivate_AlreadyActive_ThrowsDomainException()
    {
        var org = Organization.Create("Acme Corp", "acme-corp");

        DomainException ex = Assert.Throws<DomainException>(() => org.Reactivate());

        Assert.Equal("organization.already-active", ex.ErrorCode);
    }
    #endregion

    #region Mutations blocked while suspended
    [Fact]
    public void Rename_SuspendedOrganization_ThrowsDomainException()
    {
        var org = Organization.Create("Acme Corp", "acme-corp");
        org.Suspend();

        Assert.Throws<DomainException>(() => org.Rename("New Name"));
    }

    [Fact]
    public void UpdateDescription_SuspendedOrganization_ThrowsDomainException()
    {
        var org = Organization.Create("Acme Corp", "acme-corp");
        org.Suspend();

        Assert.Throws<DomainException>(() => org.UpdateDescription("New description"));
    }
    #endregion

    #region Rename / UpdateDescription - happy paths
    [Fact]
    public void Rename_ActiveOrganization_UpdatesName()
    {
        var org = Organization.Create("Acme Corp", "acme-corp");

        org.Rename("Acme Corporation");

        Assert.Equal("Acme Corporation", org.Name);
    }

    [Fact]
    public void UpdateDescription_ActiveOrganization_UpdatesDescription()
    {
        var org = Organization.Create("Acme Corp", "acme-corp");

        org.UpdateDescription("We build things.");

        Assert.Equal("We build things.", org.Description);
    }
    #endregion
}
