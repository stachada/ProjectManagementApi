using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ordinis.Testing;

/// <summary>
/// Serializer options matching the API's own wire format: web defaults (camelCase, case-insensitive
/// reads) plus enums by name. Pass to <c>ReadFromJsonAsync</c> when reading a response into a DTO —
/// the parameterless overload's defaults would expect enum ordinals.
/// </summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
