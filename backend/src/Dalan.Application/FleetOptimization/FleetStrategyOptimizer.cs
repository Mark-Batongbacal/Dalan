using System.Globalization;
using Dalan.Application.Routing;
using Dalan.Contracts.Dispatch;

namespace Dalan.Application.FleetOptimization;

public sealed partial class FleetOptimizer
{
    public const double ClusterDiameterSeconds = 600;
    public const int ClusterDeliveryLimit = 3;

    public static string WarehouseKey(GeoPoint warehouse)
        => string.Create(CultureInfo.InvariantCulture,
            $"{Math.Round(warehouse.Latitude, 6):F6},{Math.Round(warehouse.Longitude, 6):F6}");

    public static bool SameWarehouse(GeoPoint first, GeoPoint second)
        => WarehouseKey(first) == WarehouseKey(second);

    public static void ValidateOptions(FleetOptimizationRequest request)
    {
        var options = request.Options ?? new();
        if (!double.IsFinite(options.ServiceSecondsPerStop) || options.ServiceSecondsPerStop is < 0 or > 3600
            || options.MaxFleetTravelSeconds is double limit && (!double.IsFinite(limit) || limit < 0))
            throw new ArgumentException("Handling time must be 0–3600 seconds per stop; any travel budget must be finite and nonnegative.");
        if (request.Strategy == DispatchStrategy.MinimumTotalTravelTime && options.MaxFleetTravelSeconds is not null)
            throw new ArgumentException("The legacy travel strategy does not support a travel budget.");
        if (request.Strategy != DispatchStrategy.WarehouseClusterRotation) return;
        if (options.Warehouse is not { IsValid: true } warehouse
            || request.Vehicles.Any(x => !SameWarehouse(x.Start, warehouse))
            || request.Jobs.Any(x => !SameWarehouse(x.Pickup, warehouse)))
            throw new ArgumentException("Warehouse clustering requires an explicit common warehouse matching every vehicle start and delivery pickup (six decimal places).");
    }

    private static FleetOptimizationResult OptimizeSelected(
        FleetOptimizationRequest request, GeoPoint[] points, TravelMetric?[,] matrix,
        CancellationToken cancellationToken)
    {
        var routes = request.Vehicles.Select(_ => new List<IndexedStop>()).ToArray();
        var unassigned = new HashSet<int>();
        var options = request.Options ?? new();

        bool Feasible(List<IndexedStop> route, int vehicle)
        {
            if (route.Count == 0) return true;
            // Unknown load/capacity is explicitly ineligible for the new constrained strategies.
            if (request.Vehicles[vehicle].PayloadCapacityKg is not decimal capacity) return false;
            var load = 0m;
            var departed = false;
            foreach (var stop in route)
            {
                if (request.Jobs[stop.JobIndex].PayloadWeightKg is not decimal weight) return false;
                if (request.Strategy == DispatchStrategy.WarehouseClusterRotation)
                {
                    if (stop.IsPickup && departed) return false;
                    if (!stop.IsPickup) departed = true;
                }
                load += stop.IsPickup ? weight : -weight;
                if (load < 0 || load > capacity) return false;
            }
            return true;
        }

        bool WithinBudget(List<IndexedStop>[] candidate)
            => options.MaxFleetTravelSeconds is not double budget
                || candidate.Select((route, vehicle) => Score(route, vehicle, matrix).Duration).Sum() <= budget + 0.001;

        (List<IndexedStop> Stops, RouteScore Score)? Insert(List<IndexedStop> route, int vehicle, int job)
            => BestInsertion(route, vehicle, job, request.Vehicles.Count, matrix, candidate => Feasible(candidate, vehicle));

        if (request.Strategy == DispatchStrategy.WarehouseClusterRotation)
        {
            // Reserve existing assignments before flexible clusters can consume their capacity.
            foreach (var job in Enumerable.Range(0, request.Jobs.Count)
                .Where(x => request.Jobs[x].AssignedVehicleId is not null).Order())
            {
                var vehicle = request.Vehicles.ToList().FindIndex(x => x.Id == request.Jobs[job].AssignedVehicleId);
                var insertion = Insert(routes[vehicle], vehicle, job);
                var trial = routes.ToArray();
                if (insertion is not null) trial[vehicle] = insertion.Value.Stops;
                if (insertion is null || !WithinBudget(trial)) unassigned.Add(job);
                else routes = trial;
            }
            var clusters = new List<List<int>>();
            foreach (var job in Enumerable.Range(0, request.Jobs.Count).Where(x => !unassigned.Contains(x))
                .OrderBy(x => request.Jobs[x].Id, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = request.Vehicles.Count + 2 * job + 1;
                var cluster = clusters.FirstOrDefault(group => group.Count < ClusterDeliveryLimit
                    && group.All(other =>
                    {
                        var otherDestination = request.Vehicles.Count + 2 * other + 1;
                        var forward = matrix[destination, otherDestination];
                        var back = matrix[otherDestination, destination];
                        var firstFixed = request.Jobs[job].AssignedVehicleId;
                        var otherFixed = request.Jobs[other].AssignedVehicleId;
                        return forward is not null && back is not null
                            && Math.Max(forward.DurationSeconds, back.DurationSeconds) <= ClusterDiameterSeconds
                            && (firstFixed is null || otherFixed is null || firstFixed == otherFixed);
                    }));
                if (cluster is null) clusters.Add([job]);
                else cluster.Add(job);
            }
            // Membership uses complete-link destination road time. Rank separately by cheapest warehouse entry.
            var ranked = clusters.OrderBy(group => group.Min(job =>
                    matrix[0, request.Vehicles.Count + 2 * job + 1]?.DurationSeconds ?? double.PositiveInfinity))
                .ThenBy(group => request.Jobs[group[0]].Id, StringComparer.Ordinal).ToArray();
            var priorityIds = options.VehiclePriority ?? request.Vehicles.Select(x => x.Id).Order().ToArray();
            var priority = priorityIds.Select(id => request.Vehicles.ToList().FindIndex(x => x.Id == id)).ToArray();
            if (priority.Length != routes.Length || priority.Any(x => x < 0) || priority.Distinct().Count() != routes.Length)
                throw new ArgumentException("Rider priority must contain every selected vehicle exactly once.");

            bool AssignCluster(IReadOnlyList<int> group, int rank)
            {
                for (var offset = 0; offset < priority.Length; offset++)
                {
                    var vehicle = priority[(rank + offset) % priority.Length];
                    if (group.Any(job => request.Jobs[job].AssignedVehicleId is Guid fixedId
                        && fixedId != request.Vehicles[vehicle].Id)) continue;
                    var candidate = routes[vehicle];
                    var feasible = true;
                    foreach (var job in group)
                    {
                        if (candidate.Any(stop => stop.JobIndex == job)) continue;
                        var insertion = Insert(candidate, vehicle, job);
                        if (insertion is null) { feasible = false; break; }
                        candidate = insertion.Value.Stops;
                    }
                    if (!feasible) continue;
                    var trial = routes.ToArray();
                    trial[vehicle] = candidate;
                    if (!WithinBudget(trial)) continue;
                    routes[vehicle] = candidate;
                    return true;
                }
                return false;
            }

            for (var rank = 0; rank < ranked.Length; rank++)
            {
                if (AssignCluster(ranked[rank], rank)) continue;
                // Split infeasible clusters; fixed assignments and cumulative load still apply.
                foreach (var job in ranked[rank])
                    if (!AssignCluster([job], rank)) unassigned.Add(job);
            }
        }
        else
        {
            var remaining = new HashSet<int>(Enumerable.Range(0, request.Jobs.Count));
            foreach (var job in remaining.Where(x => request.Jobs[x].AssignedVehicleId is not null).Order().ToArray())
            {
                var vehicle = request.Vehicles.ToList().FindIndex(x => x.Id == request.Jobs[job].AssignedVehicleId);
                var insertion = Insert(routes[vehicle], vehicle, job);
                var trial = routes.ToArray();
                if (insertion is not null) trial[vehicle] = insertion.Value.Stops;
                if (insertion is null || !WithinBudget(trial)) unassigned.Add(job);
                else routes = trial;
                remaining.Remove(job);
            }
            while (remaining.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (int Job, List<IndexedStop>[] Routes, Objective Score)? best = null;
                foreach (var job in remaining.Order())
                for (var vehicle = 0; vehicle < routes.Length; vehicle++)
                {
                    var insertion = Insert(routes[vehicle], vehicle, job);
                    if (insertion is null) continue;
                    var trial = routes.ToArray();
                    trial[vehicle] = insertion.Value.Stops;
                    if (!WithinBudget(trial)) continue;
                    var score = FleetScore(trial, request, matrix);
                    if (best is null || score.BetterThan(best.Value.Score)) best = (job, trial, score);
                }
                if (best is null) { unassigned.UnionWith(remaining); break; }
                routes = best.Value.Routes;
                remaining.Remove(best.Value.Job);
            }
        }

        // Improve order on every route; cluster ownership is deliberately retained.
        for (var pass = 0; pass < request.Jobs.Count; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var changed = false;
            for (var source = 0; source < routes.Length; source++)
            foreach (var job in routes[source].Select(x => x.JobIndex).Distinct().ToArray())
            {
                var without = routes[source].Where(x => x.JobIndex != job).ToList();
                for (var target = 0; target < routes.Length; target++)
                {
                    if (target != source && (request.Strategy == DispatchStrategy.WarehouseClusterRotation
                        || request.Jobs[job].AssignedVehicleId is not null)) continue;
                    if (target != source && !double.IsFinite(Score(without, source, matrix).Duration)) continue;
                    var insertion = Insert(target == source ? without : routes[target], target, job);
                    if (insertion is null) continue;
                    var trial = routes.ToArray();
                    trial[source] = without;
                    trial[target] = insertion.Value.Stops;
                    if (!WithinBudget(trial) || !FleetScore(trial, request, matrix).BetterThan(FleetScore(routes, request, matrix))) continue;
                    routes = trial;
                    changed = true;
                    break;
                }
            }
            if (!changed) break;
        }

        return new FleetOptimizationResult(routes.Select((route, index) =>
        {
            var score = Score(route, index, matrix);
            return new OptimizedVehicleRoute(request.Vehicles[index].Id,
                route.Select(stop => new FleetStop(request.Jobs[stop.JobIndex].Id,
                    stop.IsPickup ? FleetStopKind.Pickup : FleetStopKind.Dropoff, points[stop.PointIndex])).ToArray(),
                score.Distance, score.Duration);
        }).ToArray(), unassigned.Order().Select(job => request.Jobs[job].Id).ToArray());
    }

    private static Objective FleetScore(List<IndexedStop>[] routes, FleetOptimizationRequest request, TravelMetric?[,] matrix)
    {
        var travel = routes.Select((route, index) => Score(route, index, matrix).Duration).ToArray();
        var workloads = travel.Select((duration, index) => duration
            + routes[index].Count * (request.Options?.ServiceSecondsPerStop ?? 0)).ToArray();
        var primary = request.Strategy == DispatchStrategy.WorkloadEquity
            ? workloads.Max() - workloads.Min() : workloads.Max();
        return new Objective(primary, travel.Sum());
    }

    private sealed record Objective(double Primary, double Total)
    {
        public bool BetterThan(Objective other) => Primary < other.Primary - 0.001
            || Math.Abs(Primary - other.Primary) <= 0.001 && Total < other.Total - 0.001;
    }
}
