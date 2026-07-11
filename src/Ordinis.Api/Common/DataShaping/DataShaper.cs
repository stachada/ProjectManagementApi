using System.Dynamic;
using System.Reflection;
using System.Text.Json;

namespace Ordinis.Api.Common.DataShaping;

/// <summary>
/// Shapes DTOs down to a caller-selected subset of properties for the <c>?fields=</c> sparse
/// fieldset query parameter (e.g. <c>?fields=id,title,status</c>). Field names are matched
/// case-insensitively against the DTO's public instance properties; unknown names are ignored.
/// <c>Id</c> is always included, even if not explicitly requested, so shaped resources remain
/// addressable by clients.
/// </summary>
public static class DataShaper
{
    /// <summary>
    /// Shapes a collection of DTOs down to the requested fields.
    /// </summary>
    /// <param name="source">The DTOs to shape.</param>
    /// <param name="fields">
    /// Comma-separated, case-insensitive property names, or <see langword="null"/>/empty to
    /// return every property unshaped.
    /// </param>
    public static IEnumerable<ExpandoObject> ShapeCollection<T>(IEnumerable<T> source, string? fields)
    {
        List<PropertyInfo> properties = GetProperties<T>(fields);
        return source.Select(item => Shape(item, properties));
    }

    /// <summary>
    /// Shapes a single DTO down to the requested fields.
    /// </summary>
    /// <param name="source">The DTO to shape.</param>
    /// <param name="fields">
    /// Comma-separated, case-insensitive property names, or <see langword="null"/>/empty to
    /// return every property unshaped.
    /// </param>
    public static ExpandoObject ShapeItem<T>(T source, string? fields)
    {
        List<PropertyInfo> properties = GetProperties<T>(fields);
        return Shape(source, properties);
    }

    private static List<PropertyInfo> GetProperties<T>(string? fields)
    {
        PropertyInfo[] allProperties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        if (string.IsNullOrWhiteSpace(fields))
        {
            return [.. allProperties];
        }

        string[] requestedNames = fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var properties = new List<PropertyInfo>();

        foreach (string name in requestedNames)
        {
            PropertyInfo? property = Array.Find(
                allProperties, p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (property is not null && !properties.Contains(property))
            {
                properties.Add(property);
            }
        }

        PropertyInfo? idProperty = Array.Find(allProperties, p => p.Name == "Id");
        if (idProperty is not null && !properties.Contains(idProperty))
        {
            properties.Insert(0, idProperty);
        }

        return properties;
    }

    private static ExpandoObject Shape<T>(T source, List<PropertyInfo> properties)
    {
        var shaped = new ExpandoObject();
        var dictionary = (IDictionary<string, object?>)shaped;

        foreach (PropertyInfo property in properties)
        {
            dictionary[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] = property.GetValue(source);
        }

        return shaped;
    }
}
