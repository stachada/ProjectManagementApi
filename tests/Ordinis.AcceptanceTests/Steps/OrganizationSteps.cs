using Ordinis.AcceptanceTests.Support;
using Ordinis.Domain.Users;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>Organization and user administration actions, and read-backs of their state.</summary>
[Binding]
internal sealed class OrganizationSteps(World world, Seeder seeder, ApiDriver api)
{
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    [When("^" + Actor + @" renames the organization ""([^""]+)"" to ""([^""]+)""$")]
    public async Task WhenPersonaRenamesTheOrganization(string persona, string organization, string newName)
    {
        Guid organizationId = world.OrganizationNamed(organization);
        string? description = (await seeder.FindOrganizationAsync(organizationId)).Description;

        ApiResponse response = await api.SendAsync(persona, HttpMethod.Put, $"/api/v1/organizations/{organizationId}",
            new { name = newName, description },
            await seeder.CurrentOrganizationVersionAsync(organizationId));

        if (response.IsSuccess)
        {
            world.Organizations[newName] = organizationId;
        }
    }

    [When("^" + Actor + @" changes the org role of (\w+) to (Viewer|Member|Admin)$")]
    public async Task WhenPersonaChangesTheOrgRole(string persona, string user, Role newOrgRole)
    {
        Guid userId = world.PersonaNamed(user).UserId;
        await api.SendAsync(persona, HttpMethod.Put, $"/api/v1/users/{userId}/org-role", new { newOrgRole },
            await seeder.CurrentUserVersionAsync(userId));
    }

    [When("^" + Actor + @" deactivates the user (\w+)$")]
    public async Task WhenPersonaDeactivatesTheUser(string persona, string user)
    {
        Guid userId = world.PersonaNamed(user).UserId;
        await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/users/{userId}/deactivate", body: null,
            await seeder.CurrentUserVersionAsync(userId));
    }

    [Then(@"^the user (\w+) has org role (Viewer|Member|Admin)$")]
    public async Task ThenTheUserHasOrgRole(string user, Role expected)
    {
        Assert.Equal(expected, (await seeder.FindUserAsync(world.PersonaNamed(user).UserId)).OrgRole);
    }

    [Then(@"^the user (\w+) is (active|inactive)$")]
    public async Task ThenTheUserIsActive(string user, string state)
    {
        Assert.Equal(state == "active", (await seeder.FindUserAsync(world.PersonaNamed(user).UserId)).IsActive);
    }
}
