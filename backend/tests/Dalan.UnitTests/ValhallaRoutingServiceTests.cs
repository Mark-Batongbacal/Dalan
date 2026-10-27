using System.Net;
using System.Text;
using System.Text.Json;
using Dalan.Application.Routing;
using Dalan.Infrastructure.Routing;
using Xunit;

namespace Dalan.UnitTests;

public sealed class ValhallaRoutingServiceTests
{
    [Fact]
    public async Task Returns_distance_duration_and_shape_from_valid_route()
    {
        using var handler = new StubHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"trip":{"summary":{"length":2.5,"time":180},"legs":[{"shape":"polyline6"}]}}""",
                Encoding.UTF8, "application/json")
        });
        var service = CreateService(handler);

        var route = await service.GetRouteAsync(new(14.6, 121.0), new(14.7, 121.1));

        Assert.Equal(new RoadRoute(2500, 180, "polyline6"), route);
        Assert.Equal("/route", handler.LastRequest!.RequestUri!.AbsolutePath);
        using var request = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("auto", request.RootElement.GetProperty("costing").GetString());
        Assert.Equal("kilometers", request.RootElement.GetProperty("units").GetString());
        Assert.Equal(14.6, request.RootElement.GetProperty("locations")[0].GetProperty("lat").GetDouble());
    }

    [Fact]
    public async Task Unreachable_route_returns_null()
    {
        using var handler = new StubHandler(_ => new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error_code":442,"error":"No path could be found"}""")
        });

        Assert.Null(await CreateService(handler).GetRouteAsync(new(14.6, 121.0), new(14.7, 121.1)));
    }

    [Fact]
    public async Task Invalid_coordinates_are_rejected_before_request()
    {
        using var handler = new StubHandler(_ => throw new Exception("HTTP must not be called"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CreateService(handler).GetRouteAsync(new(double.NaN, 121.0), new(14.7, 121.1)));
    }

    [Fact]
    public async Task Provider_failure_is_not_mistaken_for_unreachable_route()
    {
        using var handler = new StubHandler(_ => new(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateService(handler).GetRouteAsync(new(14.6, 121.0), new(14.7, 121.1)));
    }

    private static ValhallaRoutingService CreateService(HttpMessageHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("https://valhalla.example/") });

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}
