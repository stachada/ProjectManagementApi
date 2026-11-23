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

    [Given(@"^an organization ""([^""]+)"" with slug ""([^""]+)""$")]
    public Task GivenAnOrganizationWithSlug(string organization, string slug) =>
        seeder.CreateOrganizationAsync(organization, slug);

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

    [Given(@"^(\w+) is a user in ""([^""]+)"" with email ""([^""]+)"" and password ""([^""]+)""$")]
    public Task GivenUserWithEmailAndPassword(string user, string organization, string email, string password) =>
        seeder.CreatePersonaAsync(user, world.OrganizationNamed(organization), Role.Member, password, email);

    [Given(@"^(\w+) is a user in ""([^""]+)"" with org role (Viewer|Member|Admin)$")]
    public Task GivenUserWithOrgRole(string user, string organization, Role orgRole) =>
        seeder.CreatePersonaAsync(user, world.OrganizationNamed(organization), orgRole);

    [Given(@"^the user (\w+) is inactive$")]
    public Task GivenTheUserIsInactive(string user) =>
        seeder.DeactivateUserAsync(world.PersonaNamed(user).UserId);

    [Given(@"^(\w+) is a user in ""([^""]+)"" with password ""([^""]+)""$")]
    public Task GivenUserWithPassword(string user, string organization, string password) =>
        seeder.CreatePersonaAsync(user, world.OrganizationNamed(organization), Role.Member, password);

    /// <summary>Organizations are seeded active, so "is active" only states the starting point.</summary>
    [Given(@"^the organization ""([^""]+)"" is (active|suspended)$")]
    public Task GivenTheOrganizationIsInState(string organization, string state) =>
        state == "suspended"
            ? seeder.SuspendOrganizationAsync(world.OrganizationNamed(organization))
            : Task.CompletedTask;

    /// <summary>A project without boards whose (first) member is the named persona.</summary>
    [Given(@"^a project ""([^""]+)"" in ""([^""]+)"" where (\w+) is an? (Viewer|Member|Admin)$")]
    public async Task GivenAProjectWithMember(string project, string organization, string persona, Role role)
    {
        Guid organizationId = world.OrganizationNamed(organization);
        Guid projectId = await seeder.CreateProjectWithBoardsAsync(organizationId, project);

        if (!world.Personas.TryGetValue(persona, out World.Persona? member))
        {
            member = await seeder.CreatePersonaAsync(persona, organizationId, Role.Member);
        }

        await seeder.AddProjectMemberAsync(projectId, member.UserId, role);
    }

    [Given(@"^a project ""([^""]+)"" in ""([^""]+)"" described as ""([^""]+)""$")]
    public Task GivenADescribedProject(string project, string organization, string description) =>
        seeder.CreateProjectAsync(world.OrganizationNamed(organization), project, description);

    [Given(@"^project ""([^""]+)"" has a board ""([^""]+)""$")]
    public Task GivenProjectHasABoard(string project, string board) =>
        seeder.CreateBoardsAsync(world.ProjectNamed(project), board);

    [Given(@"^a project ""([^""]+)"" in ""([^""]+)"" with a board ""([^""]+)""$")]
    public Task GivenAProjectWithBoard(string project, string organization, string board) =>
        seeder.CreateProjectWithBoardsAsync(world.OrganizationNamed(organization), project, board);

    [Given(@"^a project ""([^""]+)"" in ""([^""]+)"" with boards ""([^""]+)"" and ""([^""]+)""$")]
    public Task GivenAProjectWithTwoBoards(string project, string organization, string firstBoard, string secondBoard) =>
        seeder.CreateProjectWithBoardsAsync(world.OrganizationNamed(organization), project, firstBoard, secondBoard);

    /// <summary>
    /// Seeds tasks row by row, in table order (which is therefore creation order). Columns:
    /// title and board, then optionally status (default Backlog), priority (default Medium),
    /// assignee, due in (e.g. "5 days") and description.
    /// </summary>
    [Given(@"^these tasks exist(?:, created in this order)?:$")]
    public async Task GivenTheseTasksExist(DataTable table)
    {
        foreach (DataTableRow row in table.Rows)
        {
            string dueIn = Cell(table, row, "due in");
            int? dueInDays = dueIn.Length == 0
                ? null
                : int.Parse(dueIn.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
            string priority = Cell(table, row, "priority");
            string description = Cell(table, row, "description");

            Guid taskId = await seeder.CreateTaskAsync(
                world.BoardNamed(row["board"]),
                row["title"],
                priority.Length == 0 ? Priority.Medium : Enum.Parse<Priority>(priority),
                dueInDays,
                description.Length == 0 ? null : description);

            // Assign before moving: terminal statuses refuse assignment.
            string assignee = Cell(table, row, "assignee");
            if (assignee.Length > 0)
            {
                await seeder.AssignTaskAsync(taskId, world.PersonaNamed(assignee).UserId);
            }

            string status = Cell(table, row, "status");
            if (status.Length > 0)
            {
                await seeder.SetTaskStatusAsync(taskId, Enum.Parse<ProjectTaskStatus>(status));
            }
        }
    }

    /// <summary>Generated tasks "Prefix 1" … "Prefix N", in that creation order.</summary>
    [Given(@"^(\d+) tasks on board ""([^""]+)"" titled ""(.+) 1"" to ""(.+) \d+""$")]
    public async Task GivenNumberedTasksOnBoard(int count, string board, string prefix, string samePrefix)
    {
        Assert.Equal(prefix, samePrefix);
        for (int i = 1; i <= count; i++)
        {
            await seeder.CreateTaskAsync(world.BoardNamed(board), $"{prefix} {i}");
        }
    }

    private static string Cell(DataTable table, DataTableRow row, string column) =>
        table.ContainsColumn(column) ? row[column].Trim() : string.Empty;

    [Given(@"^(\d+) tasks on board ""([^""]+)"" with priority (\w+)$")]
    public async Task GivenManyTasksOnBoard(int count, string board, Priority priority)
    {
        for (int i = 1; i <= count; i++)
        {
            await seeder.CreateTaskAsync(world.BoardNamed(board), $"Generated task {i:00}", priority);
        }
    }

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

    [Given(@"^the board ""([^""]+)"" is archived$")]
    public Task GivenTheBoardIsArchived(string board) =>
        seeder.ArchiveBoardAsync(world.BoardNamed(board));

    [Given(@"^the project ""([^""]+)"" is archived$")]
    public Task GivenTheProjectIsArchived(string project) =>
        seeder.ArchiveProjectAsync(world.ProjectNamed(project));

    [Given(@"^a task ""([^""]+)"" on board ""([^""]+)""$")]
    public Task GivenATask(string title, string board) =>
        seeder.CreateTaskAsync(world.BoardNamed(board), title);

    [Given(@"^a task ""([^""]+)"" on board ""([^""]+)"" assigned to (\w+)$")]
    public async Task GivenATaskAssignedTo(string title, string board, string assignee)
    {
        Guid taskId = await seeder.CreateTaskAsync(world.BoardNamed(board), title);
        await seeder.AssignTaskAsync(taskId, world.PersonaNamed(assignee).UserId);
    }

    [Given(@"^a task ""([^""]+)"" on board ""([^""]+)"" created by (\w+)$")]
    public Task GivenATaskCreatedBy(string title, string board, string reporter) =>
        seeder.CreateTaskAsync(world.BoardNamed(board), title, reporterId: world.PersonaNamed(reporter).UserId);

    /// <summary>
    /// A task with details given as a two-column "field | value" table without a header row
    /// (Reqnroll reads the first line as the header, so it is included). Fields: description,
    /// priority, due date ("N days from now").
    /// </summary>
    [Given(@"^a task ""([^""]+)"" on board ""([^""]+)"" with:$")]
    public Task GivenATaskWithDetails(string title, string board, DataTable details)
    {
        Dictionary<string, string> fields = new[] { (details.Header.ElementAt(0), details.Header.ElementAt(1)) }
            .Concat(details.Rows.Select(r => (r[0], r[1])))
            .ToDictionary(f => f.Item1.Trim(), f => f.Item2.Trim(), StringComparer.Ordinal);

        Priority priority = fields.TryGetValue("priority", out string? p) ? Enum.Parse<Priority>(p) : Priority.Medium;
        int? dueInDays = fields.TryGetValue("due date", out string? due)
            ? int.Parse(due.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture)
            : null;

        return seeder.CreateTaskAsync(world.BoardNamed(board), title, priority, dueInDays,
            fields.GetValueOrDefault("description"));
    }

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
