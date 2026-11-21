using Microsoft.AspNetCore.Authorization;

namespace Ordinis.Infrastructure.Security.Authorization;

/// <summary>
/// Requires the caller to be the task's assignee or a member of its project.
/// </summary>
public sealed class TaskOwnerRequirement : IAuthorizationRequirement;
