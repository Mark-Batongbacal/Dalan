namespace Dalan.Application.Routing;
public sealed record StreetAddressSuggestion(string Address);
public interface IGoogleAddressSearch
{
    Task<IReadOnlyList<StreetAddressSuggestion>> SearchAsync(string query, CancellationToken token = default);
}
