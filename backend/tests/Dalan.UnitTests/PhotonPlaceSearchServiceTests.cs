using System.Net;
using System.Text;
using Dalan.Infrastructure.Routing;
using Xunit;

namespace Dalan.UnitTests;

public sealed class PhotonPlaceSearchServiceTests
{
    [Fact]
    public async Task Search_encodes_text_and_maps_only_valid_osm_points()
    {
        using var handler = new StubHandler("""
            {"features":[
              {"geometry":{"type":"Point","coordinates":[121.03,14.6]},"properties":{"name":"A & B","city":"Manila","country":"Philippines"}},
              {"geometry":{"type":"Point","coordinates":[999,14]},"properties":{"name":"Invalid"}},
              {"geometry":{"type":"LineString","coordinates":[0,0]},"properties":{"name":"Not a point"}},
              {"geometry":{"type":42,"coordinates":[0,0]},"properties":{"name":"Not a geometry type"}},
              {"geometry":{"type":"Point","coordinates":[0,0]},"properties":{}},
              {"geometry":{"type":"Point","coordinates":["bad",0]},"properties":{"name":"Bad coordinate"}}
            ]}
            """);
        var results = await Service(handler).SearchAsync("  A & B  ");
        var result = Assert.Single(results);
        Assert.Equal("A & B, Manila, Philippines", result.Label);
        Assert.Equal(14.6, result.Location.Latitude);
        Assert.Equal(121.03, result.Location.Longitude);
        Assert.Equal("?q=A%20%26%20B&limit=5", handler.Uri!.Query);
    }

    [Fact]
    public async Task Empty_search_results_are_not_a_provider_error()
    {
        using var handler = new StubHandler("""{"features":[]}""");
        Assert.Empty(await Service(handler).SearchAsync("Unknown address"));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("   ")]
    public async Task Invalid_queries_do_not_call_provider(string query)
    {
        using var handler = new StubHandler("""{"features":[]}""");
        await Assert.ThrowsAsync<ArgumentException>(() => Service(handler).SearchAsync(query));
        Assert.Null(handler.Uri);
    }

    [Fact]
    public async Task Overlong_query_is_rejected()
    {
        using var handler = new StubHandler("""{"features":[]}""");
        await Assert.ThrowsAsync<ArgumentException>(() => Service(handler).SearchAsync(new string('a', 201)));
        Assert.Null(handler.Uri);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"features":{}}""")]
    [InlineData("[]")]
    public async Task Malformed_provider_data_is_reported(string json)
    {
        using var handler = new StubHandler(json);
        await Assert.ThrowsAsync<InvalidDataException>(() => Service(handler).SearchAsync("Manila"));
    }

    [Fact]
    public async Task Provider_failure_is_not_disguised_as_empty_results()
    {
        using var handler = new StubHandler("{}", HttpStatusCode.TooManyRequests);
        await Assert.ThrowsAsync<HttpRequestException>(() => Service(handler).SearchAsync("Manila"));
    }

    private static PhotonPlaceSearchService Service(HttpMessageHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new("https://geocoder.test/") });

    private sealed class StubHandler(string json, HttpStatusCode code = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
