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

    public static PlanningMapViewModel ForBoard(DispatchBoardViewModel board)
    {
        var targets = new List<PlanningMapTarget> { new("Common warehouse", "Settings_WarehouseLatitude", "Settings_WarehouseLongitude") };
        var overview = new List<PlanningMapPoint>();
        for (var i = 0; i < board.Vehicles.Count; i++)
        {
            var v = board.Vehicles[i];
            var prefix = $"Vehicles_{i}__";
            targets.Add(new(v.RegistrationNumber + " start", prefix + "StartLatitude", prefix + "StartLongitude",
                SelectionInputId: prefix + "Selected"));
            overview.Add(new(v.RegistrationNumber + " start", LatitudeInputId: prefix + "StartLatitude",
                LongitudeInputId: prefix + "StartLongitude", SelectionInputId: prefix + "Selected"));
        }
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
            Description = "Choose a location field, then select a map point or use its coordinates. Vehicle starts are dispatcher-confirmed locations, not live GPS.",
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
        Title = "Pickup and drop-off",
        Description = "Select a map point, search an address, or enter coordinates below. Check the address and position before saving.",
        Targets = [new("Pickup", "PickupLatitude", "PickupLongitude", "PickupAddress"),
                   new("Drop-off", "DropoffLatitude", "DropoffLongitude", "DropoffAddress")],
        Routes = [new("delivery", "Pickup to drop-off", [
            new("Pickup", LatitudeInputId: "PickupLatitude", LongitudeInputId: "PickupLongitude", Order: -1),
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
