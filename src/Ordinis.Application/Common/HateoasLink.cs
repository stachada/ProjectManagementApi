namespace Ordinis.Application.Common;

/// <summary>
/// A single HATEOAS hypermedia link embedded in a resource response's <c>_links</c> object.
/// </summary>
/// <param name="Rel">
/// The link relation name (e.g. <c>self</c>, <c>move</c>, <c>delete</c>). Identifies what the
/// linked resource/action means relative to the containing resource.
/// </param>
/// <param name="Href">The relative URI of the linked resource or action.</param>
/// <param name="Method">The HTTP method to use when following this link (e.g. <c>GET</c>, <c>POST</c>).</param>
public sealed record HateoasLink(string Rel, string Href, string Method);
