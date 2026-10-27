using Dalan.Application.Routing;

namespace Dalan.Application.FleetOptimization;

/// <summary>
/// Greedy pickup/drop-off insertion followed by local reinsertion. Minimizes estimated
/// total travel time while honoring fixed vehicle assignments and pickup precedence.
/// This heuristic does not claim a global optimum or enforce capacity/time windows.
/// </summary>
public sealed class FleetOptimizer(ITravelMatrixService travelMatrix) : IFleetOptimizer
{
    public async Task<FleetOptimizationResult> OptimizeAsync(
        FleetOptimizationRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var vehicles = request.Vehicles;
        var jobs = request.Jobs;
        var points = vehicles.Select(vehicle => vehicle.Start)
            .Concat(jobs.SelectMany(job => new[] { job.Pickup, job.Dropoff }))
            .ToArray();
        var matrix = await travelMatrix.GetMatrixAsync(points, cancellationToken);
        if (matrix.GetLength(0) != points.Length || matrix.GetLength(1) != points.Length)
            throw new InvalidDataException("Travel matrix dimensions do not match fleet points.");

        var routes = vehicles.Select(_ => new List<IndexedStop>()).ToArray();
        var unassigned = new List<string>();
        var remaining = new HashSet<int>(Enumerable.Range(0, jobs.Count));

        // Insert fixed assignments first. Each accepted route remains feasible.
        for (var jobIndex = 0; jobIndex < jobs.Count; jobIndex++)
        {
            var fixedVehicle = jobs[jobIndex].AssignedVehicleId;
            if (fixedVehicle is null) continue;
            var vehicleIndex = Array.FindIndex(vehicles.ToArray(), vehicle => vehicle.Id == fixedVehicle.Value);
            var inserted = BestInsertion(routes[vehicleIndex], vehicleIndex, jobIndex, vehicles.Count, matrix);
            if (inserted is null) unassigned.Add(jobs[jobIndex].Id);
            else routes[vehicleIndex] = inserted.Value.Stops;
            remaining.Remove(jobIndex);
        }

        // Choose the cheapest feasible job/vehicle insertion at every step.
        while (remaining.Count > 0)
        {
            (int Job, int Vehicle, List<IndexedStop> Stops, double Increment)? best = null;
            foreach (var jobIndex in remaining.Order())
            {
                for (var vehicleIndex = 0; vehicleIndex < vehicles.Count; vehicleIndex++)
                {
                    var insertion = BestInsertion(routes[vehicleIndex], vehicleIndex,
                        jobIndex, vehicles.Count, matrix);
                    if (insertion is null) continue;
                    var oldCost = Score(routes[vehicleIndex], vehicleIndex, matrix).Duration;
                    var increment = insertion.Value.Score.Duration - oldCost;
                    if (best is null || increment < best.Value.Increment - 0.001)
                        best = (jobIndex, vehicleIndex, insertion.Value.Stops, increment);
                }
            }

            if (best is null)
            {
                unassigned.AddRange(remaining.Order().Select(index => jobs[index].Id));
                break;
            }

            routes[best.Value.Vehicle] = best.Value.Stops;
            remaining.Remove(best.Value.Job);
        }

        // Reinsert each job within its selected vehicle to improve stop order.
        for (var vehicleIndex = 0; vehicleIndex < routes.Length; vehicleIndex++)
        {
            for (var pass = 0; pass < jobs.Count; pass++)
            {
                var improved = false;
                foreach (var jobIndex in routes[vehicleIndex].Select(stop => stop.JobIndex).Distinct().ToArray())
                {
                    var current = routes[vehicleIndex];
                    var without = current.Where(stop => stop.JobIndex != jobIndex).ToList();
                    var insertion = BestInsertion(without, vehicleIndex, jobIndex, vehicles.Count, matrix);
                    if (insertion is not null
                        && insertion.Value.Score.Duration < Score(current, vehicleIndex, matrix).Duration - 0.001)
                    {
                        routes[vehicleIndex] = insertion.Value.Stops;
                        improved = true;
                    }
                }
                if (!improved) break;
            }
        }

        // Reconsider vehicle choice for jobs without a fixed assignment.
        for (var pass = 0; pass < jobs.Count; pass++)
        {
            var improved = false;
            for (var jobIndex = 0; jobIndex < jobs.Count; jobIndex++)
            {
                if (jobs[jobIndex].AssignedVehicleId is not null) continue;
                var sourceIndex = Array.FindIndex(routes,
                    route => route.Any(stop => stop.JobIndex == jobIndex));
                if (sourceIndex < 0) continue;
                var source = routes[sourceIndex];
                var without = source.Where(stop => stop.JobIndex != jobIndex).ToList();
                var sourceSaving = Score(source, sourceIndex, matrix).Duration
                    - Score(without, sourceIndex, matrix).Duration;
                (int Vehicle, List<IndexedStop> Stops, double Improvement)? bestMove = null;
                for (var targetIndex = 0; targetIndex < routes.Length; targetIndex++)
                {
                    if (targetIndex == sourceIndex) continue;
                    var insertion = BestInsertion(routes[targetIndex], targetIndex,
                        jobIndex, vehicles.Count, matrix);
                    if (insertion is null) continue;
                    var added = insertion.Value.Score.Duration
                        - Score(routes[targetIndex], targetIndex, matrix).Duration;
                    var improvement = sourceSaving - added;
                    if (improvement > 0.001
                        && (bestMove is null || improvement > bestMove.Value.Improvement))
                        bestMove = (targetIndex, insertion.Value.Stops, improvement);
                }
                if (bestMove is null) continue;
                routes[sourceIndex] = without;
                routes[bestMove.Value.Vehicle] = bestMove.Value.Stops;
                improved = true;
            }
            if (!improved) break;
        }

        var output = new OptimizedVehicleRoute[vehicles.Count];
        for (var vehicleIndex = 0; vehicleIndex < vehicles.Count; vehicleIndex++)
        {
            var score = Score(routes[vehicleIndex], vehicleIndex, matrix);
            output[vehicleIndex] = new OptimizedVehicleRoute(
                vehicles[vehicleIndex].Id,
                routes[vehicleIndex].Select(stop => new FleetStop(
                    jobs[stop.JobIndex].Id,
                    stop.IsPickup ? FleetStopKind.Pickup : FleetStopKind.Dropoff,
                    points[stop.PointIndex])).ToArray(),
                score.Distance, score.Duration);
        }
        return new FleetOptimizationResult(output, unassigned);
    }

    private static (List<IndexedStop> Stops, RouteScore Score)? BestInsertion(
        List<IndexedStop> route, int vehicleIndex, int jobIndex, int vehicleCount,
        TravelMetric?[,] matrix)
    {
        (List<IndexedStop> Stops, RouteScore Score)? best = null;
        for (var pickupIndex = 0; pickupIndex <= route.Count; pickupIndex++)
        {
            for (var dropoffIndex = pickupIndex + 1; dropoffIndex <= route.Count + 1; dropoffIndex++)
            {
                var candidate = new List<IndexedStop>(route);
                candidate.Insert(pickupIndex, new(jobIndex, true, vehicleCount + 2 * jobIndex));
                candidate.Insert(dropoffIndex, new(jobIndex, false, vehicleCount + 2 * jobIndex + 1));
                var score = Score(candidate, vehicleIndex, matrix);
                if (double.IsPositiveInfinity(score.Duration)) continue;
                if (best is null || score.Duration < best.Value.Score.Duration - 0.001)
                    best = (candidate, score);
            }
        }
        return best;
    }

    private static RouteScore Score(
        List<IndexedStop> route, int vehicleIndex, TravelMetric?[,] matrix)
    {
        var previous = vehicleIndex;
        var distance = 0.0;
        var duration = 0.0;
        foreach (var stop in route)
        {
            var leg = matrix[previous, stop.PointIndex];
            if (leg is null) return new(double.PositiveInfinity, double.PositiveInfinity);
            distance += leg.DistanceMeters;
            duration += leg.DurationSeconds;
            previous = stop.PointIndex;
        }
        return new(distance, duration);
    }

    private static void Validate(FleetOptimizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Vehicles is null || request.Jobs is null
            || request.Vehicles.Count is < 1 or > 8 || request.Jobs.Count is < 1 or > 15)
            throw new ArgumentException("Provide 1-8 vehicles and 1-15 jobs.", nameof(request));
        if (request.Vehicles.Any(vehicle => vehicle is null || vehicle.Id == Guid.Empty
            || vehicle.Start is null || !vehicle.Start.IsValid)
            || request.Vehicles.Select(vehicle => vehicle.Id).Distinct().Count() != request.Vehicles.Count)
            throw new ArgumentException("Vehicle IDs and start coordinates must be valid and unique.", nameof(request));
        if (request.Jobs.Any(job => job is null || string.IsNullOrWhiteSpace(job.Id)
            || job.Pickup is null || !job.Pickup.IsValid
            || job.Dropoff is null || !job.Dropoff.IsValid
            || job.AssignedVehicleId is Guid assigned
                && request.Vehicles.All(vehicle => vehicle.Id != assigned))
            || request.Jobs.Select(job => job.Id).Distinct(StringComparer.Ordinal).Count() != request.Jobs.Count)
            throw new ArgumentException("Job IDs, coordinates, and fixed vehicle IDs must be valid and unique.", nameof(request));
    }

    private sealed record IndexedStop(int JobIndex, bool IsPickup, int PointIndex);
    private sealed record RouteScore(double Distance, double Duration);
}
