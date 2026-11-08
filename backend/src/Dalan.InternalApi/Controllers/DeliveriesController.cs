using Dalan.Application.Deliveries;
using Dalan.Contracts.Deliveries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.InternalApi.Controllers;

[ApiController]
[Route("api/deliveries")]
[Authorize(Roles = "Dispatcher")]
public sealed class DeliveriesController(IDeliveryService deliveries)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DeliveryResponse>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DeliveryResponse>>> List(
        CancellationToken cancellationToken)
    {
        var results = await deliveries.ListAsync(cancellationToken);

        return Ok(results.Select(ToResponse).ToArray());
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DeliveryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeliveryResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var delivery = await deliveries.GetByIdAsync(id, cancellationToken);

        return delivery is null
            ? NotFound()
            : Ok(ToResponse(delivery));
    }

    [HttpPost]
    [ProducesResponseType<DeliveryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeliveryResponse>> Create(
        CreateDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await deliveries.CreateAsync(
            new CreateDeliveryCommand(
                request.RecipientName,
                request.PickupAddress,
                request.PickupLatitude.GetValueOrDefault(),
                request.PickupLongitude.GetValueOrDefault(),
                request.DropoffAddress,
                request.DropoffLatitude.GetValueOrDefault(),
                request.DropoffLongitude.GetValueOrDefault(),
                request.PayloadWeightKg,
                request.ExpectedDeliveryAtUtc),
            cancellationToken);

        if (result.Status != DeliveryMutationStatus.Created)
            return FailureResponse(result);

        var response = ToResponse(result.Delivery!);

        return CreatedAtAction(
            nameof(GetById),
            new { id = response.Id },
            response);
    }

    [HttpPost("{id:guid}/assign")]
    [ProducesResponseType<DeliveryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeliveryResponse>> Assign(
        Guid id,
        AssignDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await deliveries.AssignAsync(
            id,
            new AssignDeliveryCommand(
                request.DriverId.GetValueOrDefault(),
                request.VehicleId.GetValueOrDefault()),
            cancellationToken);

        return MutationResponse(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<DeliveryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeliveryResponse>> Cancel(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await deliveries.CancelAsync(id, cancellationToken);

        return MutationResponse(result);
    }

    private ActionResult<DeliveryResponse> MutationResponse(
        DeliveryMutationResult result)
    {
        if (result.Status == DeliveryMutationStatus.Updated)
            return Ok(ToResponse(result.Delivery!));

        return FailureResponse(result);
    }

    private ActionResult<DeliveryResponse> FailureResponse(
        DeliveryMutationResult result)
        => result.Status switch
        {
            DeliveryMutationStatus.NotFound => NotFound(),

            DeliveryMutationStatus.Invalid =>
                Invalid(result.Error ?? "The delivery request is invalid."),

            DeliveryMutationStatus.Conflict =>
                Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Delivery cannot be changed",
                    detail: result.Error),

            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };

    private ActionResult<DeliveryResponse> Invalid(string error)
    {
        ModelState.AddModelError("Delivery", error);
        return ValidationProblem(ModelState);
    }

    private static DeliveryResponse ToResponse(DeliveryDetails delivery)
        => new(
            delivery.Id,
            delivery.RecipientName,
            delivery.PickupAddress,
            delivery.PickupLatitude,
            delivery.PickupLongitude,
            delivery.DropoffAddress,
            delivery.DropoffLatitude,
            delivery.DropoffLongitude,
            delivery.PayloadWeightKg,
            delivery.ExpectedDeliveryAtUtc,
            delivery.DriverId,
            delivery.VehicleId,
            delivery.Status.ToString(),
            delivery.CreatedAtUtc,
            delivery.AssignedAtUtc,
            delivery.PickedUpAtUtc,
            delivery.DeliveredAtUtc,
            delivery.CancelledAtUtc,
            delivery.Proof is null
                ? null
                : new DeliveryProofResponse(
                    delivery.Proof.Id,
                    delivery.Proof.PhotoReference,
                    delivery.Proof.RecipientConfirmation,
                    delivery.Proof.Latitude,
                    delivery.Proof.Longitude,
                    delivery.Proof.CapturedAtUtc));
}