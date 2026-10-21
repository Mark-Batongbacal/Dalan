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
