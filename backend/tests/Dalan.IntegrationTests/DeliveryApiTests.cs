using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Dalan.Application.Deliveries;
using Dalan.Contracts.Deliveries;
using Dalan.Domain.Deliveries;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dalan.IntegrationTests;

public sealed class DeliveryApiTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Admin", HttpStatusCode.Forbidden)]
    [InlineData("Dispatcher", HttpStatusCode.OK)]
    public async Task Delivery_routes_require_dispatcher(
        string? role,
        HttpStatusCode expected)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        if (role is not null)
            client.DefaultRequestHeaders.Add("X-Test-Role", role);

        using var response = await client.GetAsync("/api/deliveries");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Dispatcher_can_create_list_retrieve_assign_and_cancel()
    {
        using var factory = CreateFactory();
        using var client = CreateDispatcherClient(factory);

        using var create = await client.PostAsJsonAsync(
            "/api/deliveries",
            new CreateDeliveryRequest(
                "Maria Santos",
                "Dalan Warehouse",
                14.5995,
                120.9842,
                "Customer Address",
                14.6091,
                121.0223,
                25,
                DateTimeOffset.UtcNow.AddHours(2)));

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(create.Headers.Location);

        var created =
            await create.Content.ReadFromJsonAsync<DeliveryResponse>();

        Assert.NotNull(created);
        Assert.Equal("Pending", created.Status);
        Assert.EndsWith(
            $"/api/deliveries/{created.Id}",
            create.Headers.Location.ToString());

        using var retrieve = await client.GetAsync(create.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, retrieve.StatusCode);

        using var list = await client.GetAsync("/api/deliveries");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var deliveries =
            await list.Content.ReadFromJsonAsync<
                IReadOnlyList<DeliveryResponse>>();

        Assert.Contains(deliveries!, delivery => delivery.Id == created.Id);

        using var assign = await client.PostAsJsonAsync(
            $"/api/deliveries/{created.Id}/assign",
            new AssignDeliveryRequest(
                FakeDeliveryService.DriverId,
                FakeDeliveryService.VehicleId));

        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        var assigned =
            await assign.Content.ReadFromJsonAsync<DeliveryResponse>();

        Assert.Equal("Assigned", assigned!.Status);
        Assert.Equal(FakeDeliveryService.DriverId, assigned.DriverId);
        Assert.Equal(FakeDeliveryService.VehicleId, assigned.VehicleId);

        using var cancel = await client.PostAsync(
            $"/api/deliveries/{created.Id}/cancel",
            null);

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var cancelled =
            await cancel.Content.ReadFromJsonAsync<DeliveryResponse>();

        Assert.Equal("Cancelled", cancelled!.Status);
    }

    [Fact]
    public async Task Missing_required_coordinate_returns_validation_problem()
    {
        using var factory = CreateFactory();
        using var client = CreateDispatcherClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/deliveries",
            new
            {
                recipientName = "Maria Santos",
                pickupAddress = "Dalan Warehouse",
                pickupLongitude = 120.9842,
                dropoffAddress = "Customer Address",
                dropoffLatitude = 14.6091,
                dropoffLongitude = 121.0223
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem =
            await response.Content.ReadFromJsonAsync<
                ValidationProblemDetails>();

        Assert.Contains("PickupLatitude", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Delivery_list_filters_by_status_and_rejects_unknown_status()
    {
        using var factory = CreateFactory();
        using var client = CreateDispatcherClient(factory);

        using var pending = await client.GetAsync("/api/deliveries?status=Pending");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        var pendingItems = await pending.Content.ReadFromJsonAsync<DeliveryResponse[]>();
        var pendingItem = Assert.Single(pendingItems!);
        Assert.Equal("Pending", pendingItem.Status);

        using var delivered = await client.GetAsync("/api/deliveries?status=Delivered");
        Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
        var deliveredItems = await delivered.Content.ReadFromJsonAsync<DeliveryResponse[]>();
        Assert.Empty(deliveredItems!);

        using var invalid = await client.GetAsync("/api/deliveries?status=Unknown");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Application_validation_returns_validation_problem()
    {
        using var factory = CreateFactory();
        using var client = CreateDispatcherClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/deliveries",
            new CreateDeliveryRequest(
                "Maria Santos",
                "Dalan Warehouse",
                14.5995,
                120.9842,
                "Customer Address",
                14.6091,
                121.0223,
                0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem =
            await response.Content.ReadFromJsonAsync<
                ValidationProblemDetails>();

        Assert.Contains("Delivery", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Missing_delivery_and_assignment_conflict_are_mapped()
    {
        using var factory = CreateFactory();
        using var client = CreateDispatcherClient(factory);

        using var missing = await client.GetAsync(
            $"/api/deliveries/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var missingAssignment = await client.PostAsJsonAsync(
            $"/api/deliveries/{Guid.NewGuid()}/assign",
            new AssignDeliveryRequest(
                FakeDeliveryService.DriverId,
                FakeDeliveryService.VehicleId));

        Assert.Equal(
            HttpStatusCode.NotFound,
            missingAssignment.StatusCode);

        using var conflict = await client.PostAsJsonAsync(
            $"/api/deliveries/{FakeDeliveryService.DeliveryId}/assign",
            new AssignDeliveryRequest(
                FakeDeliveryService.ConflictDriverId,
                FakeDeliveryService.VehicleId));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var problem =
            await conflict.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal("Delivery cannot be changed", problem!.Title);
    }

    private static WebApplicationFactory<Program> CreateFactory()
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting(
                    "ConnectionStrings:Dalan",
                    "Server=localhost;Database=Unused;" +
                    "Integrated Security=True;" +
                    "TrustServerCertificate=True");

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IDeliveryService>();
                    services.AddSingleton<
                        IDeliveryService,
                        FakeDeliveryService>();

                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "Test";
                        options.DefaultChallengeScheme = "Test";
                        options.DefaultForbidScheme = "Test";
                    }).AddScheme<
                        AuthenticationSchemeOptions,
                        RoleHeaderHandler>("Test", _ => { });
                });
            });

    private static HttpClient CreateDispatcherClient(
        WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        client.DefaultRequestHeaders.Add(
            "X-Test-Role",
            "Dispatcher");

        return client;
    }

    private sealed class FakeDeliveryService : IDeliveryService
    {
        public static readonly Guid DeliveryId = Guid.NewGuid();
        public static readonly Guid DriverId = Guid.NewGuid();
        public static readonly Guid ConflictDriverId = Guid.NewGuid();
        public static readonly Guid VehicleId = Guid.NewGuid();

        private static readonly DateTimeOffset CreatedAt =
            new(2026, 11, 8, 8, 0, 0, TimeSpan.Zero);

        private DeliveryDetails delivery = NewDelivery();

        public Task<DeliveryMutationResult> CreateAsync(
            CreateDeliveryCommand command,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(command.RecipientName)
                || string.IsNullOrWhiteSpace(command.PickupAddress)
                || string.IsNullOrWhiteSpace(command.DropoffAddress)
                || command.PayloadWeightKg is <= 0)
            {
                return Task.FromResult(
                    new DeliveryMutationResult(
                        DeliveryMutationStatus.Invalid,
                        Error: "The delivery values are invalid."));
            }

            delivery = new DeliveryDetails(
                DeliveryId,
                command.RecipientName.Trim(),
                command.PickupAddress.Trim(),
                command.PickupLatitude,
                command.PickupLongitude,
                command.DropoffAddress.Trim(),
                command.DropoffLatitude,
                command.DropoffLongitude,
                command.PayloadWeightKg,
                command.ExpectedDeliveryAtUtc,
                null,
                null,
                DeliveryStatus.Pending,
                CreatedAt,
                null,
                null,
                null,
                null,
                null);

            return Task.FromResult(
                new DeliveryMutationResult(
                    DeliveryMutationStatus.Created,
                    delivery));
        }

        public Task<DeliveryMutationResult> AssignAsync(
            Guid id,
            AssignDeliveryCommand command,
            CancellationToken cancellationToken = default)
        {
            if (id != DeliveryId)
            {
                return Task.FromResult(
                    new DeliveryMutationResult(
                        DeliveryMutationStatus.NotFound));
            }

            if (command.DriverId == ConflictDriverId)
            {
                return Task.FromResult(
                    new DeliveryMutationResult(
                        DeliveryMutationStatus.Conflict,
                        Error: "The selected driver is unavailable."));
            }

            delivery = delivery with
            {
                DriverId = command.DriverId,
                VehicleId = command.VehicleId,
                Status = DeliveryStatus.Assigned,
                AssignedAtUtc = CreatedAt.AddMinutes(1)
            };

            return Task.FromResult(
                new DeliveryMutationResult(
                    DeliveryMutationStatus.Updated,
                    delivery));
        }

        public Task<DeliveryMutationResult> CancelAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            if (id != DeliveryId)
            {
                return Task.FromResult(
                    new DeliveryMutationResult(
                        DeliveryMutationStatus.NotFound));
            }

            delivery = delivery with
            {
                Status = DeliveryStatus.Cancelled,
                CancelledAtUtc = CreatedAt.AddMinutes(2)
            };

            return Task.FromResult(
                new DeliveryMutationResult(
                    DeliveryMutationStatus.Updated,
                    delivery));
        }

        public Task<DeliveryDetails?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
            => Task.FromResult<DeliveryDetails?>(
                id == DeliveryId ? delivery : null);

        public Task<IReadOnlyList<DeliveryDetails>> ListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeliveryDetails>>(
                [delivery]);

        public Task<DeliveryMutationResult> MarkPickedUpAsync(
            Guid id,
            Guid driverId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DeliveryMutationResult> CompleteAsync(
            Guid id,
            CompleteDeliveryCommand command,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private static DeliveryDetails NewDelivery()
            => new(
                DeliveryId,
                "Initial Recipient",
                "Initial Pickup",
                14.5995,
                120.9842,
                "Initial Drop-off",
                14.6091,
                121.0223,
                null,
                null,
                null,
                null,
                DeliveryStatus.Pending,
                CreatedAt,
                null,
                null,
                null,
                null,
                null);
    }

    private sealed class RoleHeaderHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            logger,
            encoder)
    {
        protected override Task<AuthenticateResult>
            HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(
                    "X-Test-Role",
                    out var role))
            {
                return Task.FromResult(
                    AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        "delivery-api-test"),
                    new Claim(
                        ClaimTypes.Role,
                        role.ToString())
                ],
                Scheme.Name);

            var principal = new ClaimsPrincipal(identity);

            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(
                        principal,
                        Scheme.Name)));
        }
    }
}
