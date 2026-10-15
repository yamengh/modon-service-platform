using FormCMS.Cms.Services;
using FormCMS.Core.Descriptors;
using Microsoft.Extensions.Primitives;

namespace Modon.Platform.Lookups;

public interface ILookupService
{
    /// <summary>Published option codes of a category, read through the FormCMS saved query.</summary>
    Task<IReadOnlySet<string>> CodesAsync(string category, CancellationToken ct);
}

public sealed class LookupService(IQueryService queries) : ILookupService
{
    public async Task<IReadOnlySet<string>> CodesAsync(string category, CancellationToken ct)
    {
        var args = new Dictionary<string, StringValues> { ["category"] = category };
        var rows = await queries.ListWithAction(LookupSchema.QueryName, new Span(), new Pagination(Limit: "1000"), args, ct);
        return rows
            .Select(r => r.TryGetValue("code", out var c) ? c?.ToString() : null)
            .OfType<string>()
            .ToHashSet();
    }
}
