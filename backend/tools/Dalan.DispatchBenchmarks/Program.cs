using System.Runtime.InteropServices;
using System.Text.Json;
using Dalan.Application.FleetOptimization;
using Dalan.Application.Routing;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var inputIndex = Array.IndexOf(args, "--input");
var outputIndex = Array.IndexOf(args, "--output");
if ((inputIndex >= 0 && inputIndex + 1 >= args.Length) || (outputIndex >= 0 && outputIndex + 1 >= args.Length))
    throw new ArgumentException("Supply a file path after --input or --output.");
var scenarios = inputIndex >= 0
    ? JsonSerializer.Deserialize<BenchmarkScenario[]>(await File.ReadAllTextAsync(args[inputIndex + 1]), jsonOptions)
        ?? throw new InvalidDataException("Supply an array of benchmark scenarios.")
    : Fixtures();
var results = new List<object>();
foreach (var scenario in scenarios)
{
    var count = scenario.Request.Vehicles.Count + 2 * scenario.Request.Jobs.Count;
    if (scenario.Matrix.Length != count || scenario.Matrix.Any(row => row.Length != count))
        throw new InvalidDataException("Matrix dimensions do not match scenario " + scenario.Name);
    var matrix = new TravelMetric?[count, count];
    for (var from = 0; from < count; from++)
    for (var to = 0; to < count; to++) matrix[from, to] = scenario.Matrix[from][to];
    results.Add(new { scenario.Name, scenario.Request, Results = await DispatchStrategyBenchmark.CompareAsync(scenario.Request, matrix) });
}
var output = JsonSerializer.Serialize(new
{
    Dataset = inputIndex >= 0 ? "Supplied recorded matrices" : "Deterministic synthetic road-time fixtures",
    Runtime = RuntimeInformation.FrameworkDescription,
    ProviderRequests = 0,
    Note = "All strategies replay identical matrices. Latencies are local CPU measurements including first-use runtime overhead; priority spread measures nearest-delivery allocation over one simulated rider cycle. These fixtures do not establish production optimality.",
    Results = results
}, jsonOptions);
if (outputIndex >= 0) await File.WriteAllTextAsync(args[outputIndex + 1], output + Environment.NewLine);
else Console.WriteLine(output);

static BenchmarkScenario[] Fixtures()
{
    GeoPoint P(double x) => new(x, 0);
    BenchmarkScenario Make(string name, int vehicles, double[] destinations, double service = 120, decimal capacity = 30, bool unreachable = false)
    {
        var fleet = Enumerable.Range(1, vehicles).Select(i => new FleetVehicle(
            Guid.Parse($"00000000-0000-0000-0000-{i:D12}"), P(0), capacity)).ToArray();
        var jobs = destinations.Select((x, i) => new FleetJob($"J{i:D2}", P(0), P(x), PayloadWeightKg: 2)).ToArray();
        var request = new FleetOptimizationRequest(fleet, jobs, Options: new(P(0), service));
        var points = fleet.Select(x => x.Start).Concat(jobs.SelectMany(x => new[] { x.Pickup, x.Dropoff })).ToArray();
        var matrix = points.Select(from => points.Select(to => unreachable && from != to && (from.Latitude == 80 || to.Latitude == 80)
            ? null : new TravelMetric(Math.Abs(from.Latitude - to.Latitude) * 1000, Math.Abs(from.Latitude - to.Latitude) * 100)).ToArray()).ToArray();
        return new(name, request, matrix);
    }
    return
    [
        Make("handling-dominates", 2, [1, 2, 3, 4]),
        Make("warehouse-separated-clusters", 3, [-12, -11, -10, 1, 2, 3, 16, 17]),
        Make("sparse-fleet", 5, [1, 8, 15]),
        Make("capacity-pressure", 2, [1, 2, 3, 4, 5, 6, 7], capacity: 4),
        Make("unreachable-destination", 3, [1, 2, 10, 80], unreachable: true),
        Make("current-size-boundary", 8, Enumerable.Range(1, 15).Select(x => x * 1.2).ToArray()),
        Make("driving-only", 2, [1, 2, 3, 4], service: 0)
    ];
}

public sealed record BenchmarkScenario(string Name, FleetOptimizationRequest Request, TravelMetric?[][] Matrix);
