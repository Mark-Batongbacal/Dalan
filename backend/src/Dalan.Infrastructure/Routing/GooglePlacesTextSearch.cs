using System.Net.Http.Json;
using System.Text.Json;
using Dalan.Application.Routing;

namespace Dalan.Infrastructure.Routing;

public sealed class GooglePlacesTextSearch(HttpClient client, string apiKey) : IGooglePlacesSearch
{
    public async Task<IReadOnlyList<PlaceTextResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        query = query?.Trim() ?? "";
        if (query.Length is < 3 or > 200) throw new ArgumentException("Enter 3–200 characters.", nameof(query));
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/places:searchText")
        { Content = JsonContent.Create(new { textQuery = query, pageSize = 10 }) };
        request.Headers.Add("X-Goog-Api-Key", apiKey);
        request.Headers.Add("X-Goog-FieldMask", "places.displayName,places.formattedAddress,places.googleMapsUri,places.attributions");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!body.RootElement.TryGetProperty("places", out var places)) return [];
        var results = new List<PlaceTextResult>();
        foreach (var place in places.EnumerateArray().Take(10))
        {
            var name = place.TryGetProperty("displayName", out var display) && display.TryGetProperty("text", out var text) ? text.GetString() : null;
            var address = place.TryGetProperty("formattedAddress", out var formatted) ? formatted.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("The place response was incomplete.");
            var maps = place.TryGetProperty("googleMapsUri", out var map) ? SafeUri(map.GetString(), googleOnly: true) : null;
            var attributions = new List<PlaceTextAttribution>();
            if (place.TryGetProperty("attributions", out var credits))
                foreach (var credit in credits.EnumerateArray())
                    if (credit.TryGetProperty("provider", out var provider) && provider.GetString() is string providerName)
                        attributions.Add(new(providerName, credit.TryGetProperty("providerUri", out var uri) ? SafeUri(uri.GetString()) : null));
            results.Add(new(name, address ?? "", maps, attributions));
        }
        return results;
    }

    private static string? SafeUri(string? value, bool googleOnly = false)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        if (googleOnly && uri.Host is not ("www.google.com" or "maps.google.com" or "google.com")) return null;
        return uri.AbsoluteUri;
    }
}
