using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ordinis.AcceptanceTests.Support;

/// <summary>
/// Sends HTTP requests to the in-process API as a named persona (with a real JWT) or
/// anonymously. This is the only place that knows about routes, headers and JSON — step
/// definitions stay in the business language of the feature files.
/// </summary>
internal sealed class ApiDriver(World world, Seeder seeder)
{
    /// <summary>The sentinel persona name for unauthenticated requests.</summary>
    public const string Anonymous = "an anonymous caller";

    /// <summary>
    /// Sends a request and records it as <see cref="World.LastResponse"/>.
    /// </summary>
    /// <param name="persona">Persona name, or <see cref="Anonymous"/>.</param>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">Path relative to the API root, e.g. <c>/api/v1/tasks</c>.</param>
    /// <param name="body">Optional JSON body (serialised with ASP.NET Core's web defaults).</param>
    /// <param name="ifMatch">Optional, already-quoted ETag for the <c>If-Match</c> header.</param>
    public async Task<ApiResponse> SendAsync(string persona, HttpMethod method, string path, object? body = null, string? ifMatch = null)
    {
        using HttpClient client = TestHost.Factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);

        if (persona != Anonymous)
        {
            string token = world.SessionTokens.TryGetValue(persona, out string? sessionToken)
                ? sessionToken
                : await seeder.AccessTokenForAsync(world.PersonaNamed(persona).UserId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        ApiResponse result = await SendRawAsync(client, request);
        world.LastResponse = result;
        return result;
    }

    /// <summary>
    /// Uploads a file as <c>multipart/form-data</c> (field <c>file</c>) and records the response.
    /// </summary>
    public async Task<ApiResponse> UploadAsync(string persona, string path, string fileName, string contentType, byte[] content)
    {
        using HttpClient client = TestHost.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await seeder.AccessTokenForAsync(world.PersonaNamed(persona).UserId));

        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Content = new MultipartFormDataContent { { file, "file", fileName } };

        ApiResponse result = await SendRawAsync(client, request);
        world.LastResponse = result;
        return result;
    }

    /// <summary>Sends a request as an arbitrary user ID (used for hidden observer reads).</summary>
    public async Task<ApiResponse> SendAsUserAsync(Guid userId, HttpMethod method, string path)
    {
        using HttpClient client = TestHost.Factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await seeder.AccessTokenForAsync(userId));
        return await SendRawAsync(client, request);
    }

    private static async Task<ApiResponse> SendRawAsync(HttpClient client, HttpRequestMessage request)
    {
        using HttpResponseMessage response = await client.SendAsync(request);

        int? totalCount = response.Headers.TryGetValues("X-Total-Count", out IEnumerable<string>? values)
            && int.TryParse(values.FirstOrDefault(), out int count)
                ? count
                : null;

        return new ApiResponse(
            response.StatusCode,
            await response.Content.ReadAsStringAsync(),
            response.Headers.ETag?.Tag,
            response.Headers.Location,
            totalCount);
    }
}

/// <summary>A captured HTTP response, decoupled from the disposed <see cref="HttpResponseMessage"/>.</summary>
internal sealed record ApiResponse(HttpStatusCode StatusCode, string Body, string? ETag, Uri? Location, int? TotalCount)
{
    public bool IsSuccess => (int)StatusCode is >= 200 and < 300;

    /// <summary>The Problem Details <c>type</c>, or <see langword="null"/> if the body is not Problem Details.</summary>
    public string? ProblemType => TryGetString("type");

    /// <summary>The field names in a validation Problem Details <c>errors</c> map.</summary>
    public IReadOnlyList<string> ValidationErrorFields
    {
        get
        {
            JsonElement? root = TryParse();
            if (root is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty("errors", out JsonElement errors)
                && errors.ValueKind == JsonValueKind.Object)
            {
                return errors.EnumerateObject().Select(p => p.Name).ToList();
            }

            return [];
        }
    }

    public JsonElement Json => TryParse() ?? throw new InvalidOperationException($"Response body is not JSON: {Body}");

    /// <summary>A readable one-liner for assertion messages.</summary>
    public override string ToString() => $"{(int)StatusCode} {StatusCode}: {Body}";

    private string? TryGetString(string property) =>
        TryParse() is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty(property, out JsonElement value)
            ? value.GetString()
            : null;

    private JsonElement? TryParse()
    {
        if (string.IsNullOrWhiteSpace(Body))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(Body).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
