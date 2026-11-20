using Dalan.Application.Routing;

namespace Dalan.Web.Models;

public sealed record PlanningMapPoint(string Label, double? Latitude = null, double? Longitude = null,
    string? LatitudeInputId = null, string? LongitudeInputId = null, string? SelectionInputId = null,
    string? VehicleInputId = null, string? OrderInputId = null, int Order = 0);
public sealed record PlanningMapTarget(string Label, string LatitudeInputId, string LongitudeInputId,
    string? AddressInputId = null, string? SelectionInputId = null);
public sealed record PlanningMapRoute(string Id, string Label, IReadOnlyList<PlanningMapPoint> Points);
public sealed record PlanningMapDelivery(GeoPoint Pickup, GeoPoint Dropoff);

public sealed class PlanningMapViewModel
{
    public string Title { get; init; } = "Planning map";
    public string Description { get; init; } = "Stops are shown in route order. Load road geometry for a selected route; this does not change or confirm the dispatch.";
    public bool Preview { get; init; }
    public IReadOnlyList<PlanningMapTarget> Targets { get; init; } = [];
    public IReadOnlyList<PlanningMapPoint> Overview { get; init; } = [];
    public IReadOnlyList<PlanningMapRoute> Routes { get; init; } = [];

    public static PlanningMapViewModel ForPlanner(FleetPlannerViewModel planner)
    {
        var targets = planner.Vehicles.Select((vehicle, i) => new PlanningMapTarget(
            vehicle.RegistrationNumber + " start", $"Vehicles_{i}__StartLatitude", $"Vehicles_{i}__StartLongitude",
            SelectionInputId: $"Vehicles_{i}__Selected")).ToList();
        for (var i = 0; i < planner.Jobs.Count; i++)
        {
            targets.Add(new($"Job {i + 1} pickup", $"Jobs_{i}__PickupLatitude", $"Jobs_{i}__PickupLongitude"));
            targets.Add(new($"Job {i + 1} drop-off", $"Jobs_{i}__DropoffLatitude", $"Jobs_{i}__DropoffLongitude"));
        }
        return new()
        {
            Title = "Choose vehicle starts and job locations",
            Description = "Choose a location to set, search an address or position the map, then confirm. This planner previews jobs without saving or dispatching them.",
            Targets = targets,
            Overview = targets.Select(target => new PlanningMapPoint(target.Label,
                LatitudeInputId: target.LatitudeInputId, LongitudeInputId: target.LongitudeInputId)).ToArray()
        };
    }

    public static PlanningMapViewModel ForBoard(DispatchBoardViewModel board)
    {
        var targets = new List<PlanningMapTarget> { new("Warehouse", "Settings_WarehouseLatitude", "Settings_WarehouseLongitude", "Settings_WarehouseAddress") };
        var overview = new List<PlanningMapPoint>
        {
            new("Warehouse", LatitudeInputId: "Settings_WarehouseLatitude",
                LongitudeInputId: "Settings_WarehouseLongitude", Order: -1)
        };
        for (var i = 0; i < board.Deliveries.Count; i++)
        {
            var d = board.Deliveries[i];
            var selected = $"Deliveries_{i}__Selected";
            overview.Add(new("Pickup: " + d.RecipientName, d.PickupLatitude, d.PickupLongitude, SelectionInputId: selected));
            overview.Add(new("Drop-off: " + d.RecipientName, d.DropoffLatitude, d.DropoffLongitude, SelectionInputId: selected));
        }
        var names = board.Deliveries.ToDictionary(x => x.Id, x => x.RecipientName);
        var vehicles = board.Vehicles.ToDictionary(x => x.Id, x => x.RegistrationNumber);
        return new()
        {
            Title = "Dispatch planning map",
            Description = "Select the warehouse on the map, search its address, or enter coordinates. Every selected vehicle starts at this warehouse.",
            Preview = board.Preview is not null, Targets = targets, Overview = overview,
            Routes = board.Preview?.Routes.Select(r => new PlanningMapRoute(r.VehicleId.ToString(),
                vehicles.GetValueOrDefault(r.VehicleId) ?? "Vehicle",
                new[] { new PlanningMapPoint("Start", r.Start.Latitude, r.Start.Longitude, Order: -1) }
                    .Concat(r.Stops.Select((s, i) => new PlanningMapPoint(
                        $"{s.Kind}: {names.GetValueOrDefault(s.DeliveryId) ?? s.DeliveryId.ToString()}",
                        s.Location.Latitude, s.Location.Longitude, Order: i))).ToArray())).ToArray() ?? []
        };
    }

    public static PlanningMapViewModel ForDelivery() => new()
    {
        Title = "Warehouse and drop-off",
        Description = "Use your saved warehouse as pickup and confirm the delivery destination. Change your default warehouse from the dispatch workspace.",
        Targets = [new("Warehouse", "PickupLatitude", "PickupLongitude", "PickupAddress"),
                   new("Drop-off", "DropoffLatitude", "DropoffLongitude", "DropoffAddress")],
        Routes = [new("delivery", "Warehouse to drop-off", [
            new("Warehouse", LatitudeInputId: "PickupLatitude", LongitudeInputId: "PickupLongitude", Order: -1),
            new("Drop-off", LatitudeInputId: "DropoffLatitude", LongitudeInputId: "DropoffLongitude")])]
    };

    public static PlanningMapViewModel ForReview(DispatchReviewViewModel review)
    {
        var stops = review.Stops.Select((s, i) =>
        {
            if (!review.DeliveryLocations.TryGetValue(s.DeliveryId, out var locations)) return null;
            var point = s.Kind == "Pickup" ? locations.Pickup : locations.Dropoff;
            return new PlanningMapPoint(
                $"{s.Kind}: {review.DeliveryNames.GetValueOrDefault(s.DeliveryId) ?? s.DeliveryId.ToString()}",
                point.Latitude, point.Longitude, VehicleInputId: $"Stops_{i}__VehicleId", OrderInputId: $"Stops_{i}__Order");
        }).OfType<PlanningMapPoint>().ToArray();
        return new()
        {
            Title = "Review route map",
            Routes = review.Vehicles.Select(v => new PlanningMapRoute(v.VehicleId.ToString(),
                review.VehicleNames.GetValueOrDefault(v.VehicleId) ?? "Vehicle",
                new[] { new PlanningMapPoint("Start", v.StartLatitude, v.StartLongitude, Order: -1) }
                    .Concat(stops).ToArray())).ToArray()
        };
    }

    public static PlanningMapViewModel ForPlan(DispatchPlanPageViewModel page) => new()
    {
        Title = "Confirmed route map",
        Description = "Saved stop positions and sequence. Load road geometry on demand; newer road data may differ from the saved dispatch estimates.",
        Routes = page.Plan.Routes.Select(r => new PlanningMapRoute(r.VehicleId.ToString(),
            page.VehicleRegistrations.GetValueOrDefault(r.VehicleId) ?? "Vehicle",
            new[] { new PlanningMapPoint("Start", r.StartLatitude, r.StartLongitude, Order: -1) }
                .Concat(r.Stops.OrderBy(s => s.Position).Select(s => new PlanningMapPoint(
                    $"{s.Kind}: {page.Deliveries.GetValueOrDefault(s.DeliveryId)?.RecipientName ?? s.DeliveryId.ToString()}",
                    s.Latitude, s.Longitude, Order: s.Position))).ToArray())).ToArray()
    };
}
