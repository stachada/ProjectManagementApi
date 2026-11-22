using FluentValidation;
using FluentValidation.Results;
using Ordinis.Application.Common;
using Ordinis.Application.Organizations.Commands;
using Ordinis.Application.Users.Commands;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;

namespace Ordinis.UnitTests.Application.Common;

/// <summary>
/// GAP-12: user and organization writes use the same optimistic concurrency as every other
/// aggregate, so each of their seven write commands requires an <c>If-Match</c> token.
/// </summary>
public sealed class IfMatchRequiredValidatorTests
{
    private static readonly byte[] Token = [1, 2, 3, 4];

    public static TheoryData<string> WriteCommands() =>
        ["UpdateUser", "ChangeUserOrgRole", "DeactivateUser", "ReactivateUser",
         "UpdateOrganization", "SuspendOrganization", "ReactivateOrganization"];

    [Theory]
    [MemberData(nameof(WriteCommands))]
    public async Task Validate_WithoutIfMatch_HasIfMatchError(string command)
    {
        ValidationResult result = await ValidateAsync(command, ifMatch: null);

        Assert.Contains(result.Errors, e => e.PropertyName == "IfMatch" && e.ErrorMessage == "If-Match header is required.");
    }

    [Theory]
    [MemberData(nameof(WriteCommands))]
    public async Task Validate_WithIfMatch_HasNoIfMatchError(string command)
    {
        ValidationResult result = await ValidateAsync(command, Token);

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == "IfMatch");
    }

    private static async Task<ValidationResult> ValidateAsync(string command, byte[]? ifMatch)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Guid id = Guid.CreateVersion7();
        Guid requester = Guid.CreateVersion7();

        return command switch
        {
            "UpdateUser" => await new UpdateUserValidator().ValidateAsync(new UpdateUser(id, "Nora", requester, ifMatch)),
            "ChangeUserOrgRole" => await new ChangeUserOrgRoleValidator().ValidateAsync(new ChangeUserOrgRole(id, Role.Member, requester, ifMatch)),
            "DeactivateUser" => await new DeactivateUserValidator().ValidateAsync(new DeactivateUser(id, requester, ifMatch)),
            "ReactivateUser" => await new ReactivateUserValidator().ValidateAsync(new ReactivateUser(id, requester, ifMatch)),
            "UpdateOrganization" => await new UpdateOrganizationValidator(db, new SlugGenerator()).ValidateAsync(new UpdateOrganization(id, "Acme", null, ifMatch)),
            "SuspendOrganization" => await new SuspendOrganizationValidator().ValidateAsync(new SuspendOrganization(id, ifMatch)),
            "ReactivateOrganization" => await new ReactivateOrganizationValidator().ValidateAsync(new ReactivateOrganization(id, ifMatch)),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
    }
}
