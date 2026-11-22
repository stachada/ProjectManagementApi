using Ordinis.AcceptanceTests.Support;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// "Given" steps that arrange organizations, personas, projects, boards and tasks.
/// All of them seed through the domain model (see <see cref="Seeder"/>), not the API.
/// </summary>
[Binding]
internal sealed class SetupSteps(World world, Seeder seeder)
{
    [Given(@"^an organization ""([^""]+)""$")]
    public Task GivenAnOrganization(string organization) =>
        seeder.CreateOrganizationAsync(organization);

    [Given(@"^an organization ""([^""]+)"" whose org Admin is (\w+)$")]
    public async Task GivenAnOrganizationWithAdmin(string organization, string admin)
    {
        Guid organizationId = await seeder.CreateOrganizationAsync(organization);
        await seeder.CreatePersonaAsync(admin, organizationId, Role.Admin);
    }

    [Given(@"^an organization ""([^""]+)"" with a user (\w+)$")]
    public async Task GivenAnOrganizationWithUser(string organization, string user)
    {
        Guid organizationId = await seeder.CreateOrganizationAsync(organization);
        await seeder.CreatePersonaAsync(user, organizationId, Role.Member);
    }

    [Given(@"^(\w+) is a user in ""([^""]+)""$")]
    public Task GivenUserInOrganization(string user, string organization) =>
        seeder.CreatePersonaAsync(user, world.OrganizationNamed(organization), Role.Member);

    [Given(@"^a project ""([^""]+)"" in ""([^""]+)"" with a board ""([^""]+)""$")]
    public Task GivenAProjectWithBoard(string project, string organization, string board) =>
        seeder.CreateProjectWithBoardAsync(world.OrganizationNamed(organization), project, board);

    [Given(@"^(\w+) is an? (Viewer|Member|Admin) of project ""([^""]+)""$")]
    public async Task GivenProjectMember(string persona, Role role, string project)
    {
        Guid projectId = world.ProjectNamed(project);

        if (!world.Personas.TryGetValue(persona, out World.Persona? member))
        {
            // Personas first mentioned as project members belong to the project's organization.
            member = await seeder.CreatePersonaAsync(persona, world.ProjectOrganizations[projectId], Role.Member);
        }

        await seeder.AddProjectMemberAsync(projectId, member.UserId, role);
    }

    [Given(@"^a task ""([^""]+)"" on board ""([^""]+)""$")]
    public Task GivenATask(string title, string board) =>
        seeder.CreateTaskAsync(world.BoardNamed(board), title);

    [Given(@"^the task ""([^""]+)"" has status (\w+)$")]
    public Task GivenTaskHasStatus(string title, ProjectTaskStatus status) =>
        seeder.SetTaskStatusAsync(world.TaskNamed(title), status);

    [Given(@"^the task ""([^""]+)"" is assigned to (\w+)$")]
    public Task GivenTaskIsAssigned(string title, string assignee) =>
        seeder.AssignTaskAsync(world.TaskNamed(title), world.PersonaNamed(assignee).UserId);

    [Given(@"^project ""([^""]+)"" has a webhook subscribed to ""([^""]+)""$")]
    public Task GivenProjectHasWebhook(string project, string eventType) =>
        seeder.RegisterWebhookAsync(
            world.ProjectNamed(project),
            TestHost.Receiver.UrlFor(WebhookSteps.DefaultReceiver),
            [eventType]);
}
