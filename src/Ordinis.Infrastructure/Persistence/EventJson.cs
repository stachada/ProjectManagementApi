using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ordinis.Infrastructure.Persistence;

/// <summary>
/// The one set of serializer options for domain-event JSON: outbox payloads (which the audit
/// trail exposes as-is) and webhook envelopes. Enums are written by name, matching the HTTP API;
/// the converter still reads numbers, so outbox rows written before names were used stay readable.
/// Property names keep their declared casing — the audit query reads <c>$.TaskId</c> directly.
/// </summary>
public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
