using Dalan.Application.Deliveries;
using Dalan.Domain.Dispatch;

namespace Dalan.Web.Models;

public sealed record DispatchPlanPageViewModel(
    DispatchPlan Plan,
    IReadOnlyDictionary<Guid, DeliveryDetails> Deliveries,
    IReadOnlyDictionary<Guid, string> DriverNames,
    IReadOnlyDictionary<Guid, string> VehicleRegistrations);
