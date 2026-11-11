using System.Net;
using System.Text;
using System.Text.Json;
using Dalan.Application.Routing;
using Dalan.Infrastructure.Routing;
using Xunit;

namespace Dalan.UnitTests;

public sealed class ValhallaTravelMatrixServiceTests
{
    [Fact]
    public async Task Repeated_warehouse_coordinates_share_one_provider_location()
    {
        using var handler = new StubHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://valhalla.example/") };
        var result = await new ValhallaTravelMatrixService(client).GetMatrixAsync(
            [new(14.6, 121), new(14.6, 121), new(14.7, 121.1), new(14.6, 121)]);
        using var request = JsonDocument.Parse(handler.Body!);
        Assert.Equal(2, request.RootElement.GetProperty("sources").GetArrayLength());
        Assert.Equal(new TravelMetric(1200, 12), result[1, 2]);
        Assert.Null(result[2, 3]);
        Assert.Equal(new TravelMetric(0, 0), result[0, 3]);
    }

    [Fact]
    public async Task Provider_cell_limits_tile_one_matrix_without_losing_direction_or_order()
    {
        using var handler = new BlockHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://valhalla.example/") };
        var result = await new ValhallaTravelMatrixService(client, new(4)).GetMatrixAsync(
            [new(1, 0), new(2, 0), new(3, 0), new(1, 0)]);
        Assert.Equal(4, handler.Calls);
        Assert.Equal(new TravelMetric(13000, 13), result[0, 2]);
        Assert.Equal(new TravelMetric(31000, 31), result[2, 3]);
    }

    private sealed class BlockHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var sources = body.RootElement.GetProperty("sources").EnumerateArray().Select(x => x.GetProperty("lat").GetDouble()).ToArray();
            var targets = body.RootElement.GetProperty("targets").EnumerateArray().Select(x => x.GetProperty("lat").GetDouble()).ToArray();
            Assert.InRange(sources.Length * targets.Length, 1, 4);
            var values = sources.Select(from => targets.Select(to => from * 10 + to).ToArray()).ToArray();
            var json = JsonSerializer.Serialize(new { sources_to_targets = new { durations = values, distances = values } });
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Reads_directed_durations_distances_and_unreachable_pairs()
    {
        using var handler = new StubHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://valhalla.example/") };

        var result = await new ValhallaTravelMatrixService(client).GetMatrixAsync(
            [new(14.6, 121.0), new(14.7, 121.1)]);

        Assert.Equal(new TravelMetric(1200, 12), result[0, 1]);
        Assert.Null(result[1, 0]);
        Assert.Equal("/sources_to_targets", handler.Path);
        using var request = JsonDocument.Parse(handler.Body!);
        Assert.False(request.RootElement.GetProperty("verbose").GetBoolean());
        Assert.Equal("auto", request.RootElement.GetProperty("costing").GetString());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? Path { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"sources_to_targets":{"durations":[[0,12],[null,0]],"distances":[[0,1.2],[null,0]]}}""",
                    Encoding.UTF8, "application/json")
            };
        }
    }
}
