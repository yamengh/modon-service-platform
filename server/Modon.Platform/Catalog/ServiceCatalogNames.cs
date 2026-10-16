using FormCMS.Cms.Services;
using FormCMS.Core.Descriptors;
using Microsoft.Extensions.Primitives;

namespace Modon.Platform.Catalog;

/// <summary>
/// Service names come from the FormCMS catalogue (saved query), so admin edits show up everywhere.
/// </summary>
public sealed class ServiceCatalogNames(IQueryService queries)
{
    private Dictionary<string, (string Ar, string En)>? _cache;

    public async Task<(string Ar, string En)> NameOfAsync(string serviceCode, CancellationToken ct)
        => (await AllAsync(ct)).GetValueOrDefault(serviceCode, (serviceCode, serviceCode));

    /// <summary>Loaded once per HTTP request (the class is scoped).</summary>
    public async Task<Dictionary<string, (string Ar, string En)>> AllAsync(CancellationToken ct)
    {
        if (_cache is not null) return _cache;
        var rows = await queries.ListWithAction(ServiceCatalogSchema.ListQueryName, new Span(),
            new Pagination(Limit: "100"), new Dictionary<string, StringValues>(), ct);
        return _cache = rows
            .Where(x => x.ContainsKey("code"))
            .ToDictionary(
                x => x["code"].ToString()!,
                x => (x.TryGetValue("nameAr", out var a) ? a?.ToString() ?? "" : "",
                      x.TryGetValue("nameEn", out var e) ? e?.ToString() ?? "" : ""));
    }
}
