using System.Net.Http.Json;
using System.Text.Json;
using Dalan.Application.Routing;
namespace Dalan.Infrastructure.Routing;
// Only selected end-user street addresses may enter a transaction. No POIs, place IDs,
// Google geometry or prediction lists are persisted or passed to the map/routing provider.
public sealed class GoogleAddressSearch(HttpClient client, string key) : IGoogleAddressSearch
{
    public async Task<IReadOnlyList<StreetAddressSuggestion>> SearchAsync(string query, CancellationToken token = default)
    {
        query = query.Trim(); if (query.Length is < 3 or > 200) throw new ArgumentException("Enter 3–200 characters.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/places:autocomplete")
        {
            Content = JsonContent.Create(new { input = query, includedPrimaryTypes = new[] { "street_address" }, includeQueryPredictions = false })
        };
        request.Headers.Add("X-Goog-Api-Key", key);
        request.Headers.Add("X-Goog-FieldMask", "suggestions.placePrediction.text.text,suggestions.placePrediction.types");
        using var response = await client.SendAsync(request, token); response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        var results = new List<StreetAddressSuggestion>();
        if (!body.RootElement.TryGetProperty("suggestions", out var suggestions)) return results;
        foreach (var entry in suggestions.EnumerateArray().Take(5))
        {
            if (!entry.TryGetProperty("placePrediction", out var place) || !place.TryGetProperty("types", out var types)) continue;
            var values = types.EnumerateArray().Select(x => x.GetString()).ToArray();
            if (!values.Contains("street_address") || values.Contains("point_of_interest") || values.Contains("establishment")) continue;
            if (place.TryGetProperty("text", out var text) && text.TryGetProperty("text", out var address)
                && address.GetString() is { Length: > 0 and <= 500 } value) results.Add(new(value));
        }
        return results;
    }
}
