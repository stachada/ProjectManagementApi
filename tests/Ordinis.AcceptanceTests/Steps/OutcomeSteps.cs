using System.Net;
using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// The shared "Then the request …" vocabulary from the README step glossary, mapping business
/// outcomes to HTTP status codes and Problem Details.
/// </summary>
[Binding]
internal sealed class OutcomeSteps(World world)
{
    private static readonly Dictionary<string, HttpStatusCode> Outcomes = new(StringComparer.Ordinal)
    {
        ["malformed"] = HttpStatusCode.BadRequest,
        ["unauthenticated"] = HttpStatusCode.Unauthorized,
        ["forbidden"] = HttpStatusCode.Forbidden,
        ["not found"] = HttpStatusCode.NotFound,
        ["conflicting"] = HttpStatusCode.Conflict,
        ["invalid"] = HttpStatusCode.UnprocessableEntity,
        ["too many requests"] = HttpStatusCode.TooManyRequests,
    };

    [Then(@"^the request succeeds$")]
    public void ThenTheRequestSucceeds()
    {
        Assert.True(world.Response.IsSuccess, $"Expected a successful response, got {world.Response}");
    }

    [Then(@"^the request is rejected as (malformed|unauthenticated|forbidden|not found|conflicting|invalid|too many requests)$")]
    public void ThenTheRequestIsRejectedAs(string outcome)
    {
        HttpStatusCode expected = Outcomes[outcome];
        Assert.True(
            world.Response.StatusCode == expected,
            $"Expected {(int)expected} {expected} ({outcome}), got {world.Response}");
        AssertIsProblemDetails();
    }

    [Then(@"^the request is rejected as invalid with error ""([^""]+)""$")]
    public void ThenTheRequestIsRejectedWithError(string errorCode)
    {
        ThenTheRequestIsRejectedAs("invalid");
        Assert.True(world.Response.ProblemType == $"urn:ordinis:error:{errorCode}", world.Response.ToString());
    }

    [Then(@"^the request is rejected as invalid on field ""([^""]+)""$")]
    public void ThenTheRequestIsRejectedOnField(string field)
    {
        ThenTheRequestIsRejectedAs("invalid");
        Assert.Contains(world.Response.ValidationErrorFields, f => string.Equals(f, field, StringComparison.OrdinalIgnoreCase));
    }

    private void AssertIsProblemDetails()
    {
        Assert.True(
            world.Response.ProblemType is not null,
            $"Expected a Problem Details body, got {world.Response}");
    }
}
