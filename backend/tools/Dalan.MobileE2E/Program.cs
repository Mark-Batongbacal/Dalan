using System.Security.Cryptography;
using System.Text.Json;
using Dalan.Contracts.FleetOptimization;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var connection = Environment.GetEnvironmentVariable("DALAN_MOBILE_E2E_SQL")
    ?? throw new InvalidOperationException("Set DALAN_MOBILE_E2E_SQL to an isolated local SQL test database.");
var sql = new SqlConnectionStringBuilder(connection);
if (!sql.InitialCatalog.StartsWith("DalanMobileE2E_", StringComparison.Ordinal)
    || !(sql.DataSource.StartsWith("127.0.0.1,", StringComparison.Ordinal) || sql.DataSource.StartsWith("localhost,", StringComparison.Ordinal)))
    throw new InvalidOperationException("This fixture operates only on local DalanMobileE2E_* databases.");
var manifestPath = Environment.GetEnvironmentVariable("DALAN_MOBILE_E2E_MANIFEST")
    ?? throw new InvalidOperationException("Set DALAN_MOBILE_E2E_MANIFEST to a private temporary file path.");
var services = new ServiceCollection().AddLogging();
services.AddDbContext<DalanDbContext>(options => options.UseSqlServer(connection));
services.AddIdentityCore<IdentityUser>(options => options.Password.RequiredLength = 12)
    .AddRoles<IdentityRole>().AddEntityFrameworkStores<DalanDbContext>();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<DalanDbContext>();
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };

if (args is ["seed"])
{
    if (File.Exists(manifestPath)) throw new InvalidOperationException("Use a new private manifest file for each fixture.");
    await db.Database.MigrateAsync();
    var suffix = Guid.NewGuid().ToString("N")[..10];
    var password = "E2e!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Dispatcher", "Driver" })
        Require(await roles.CreateAsync(new IdentityRole(role)));
    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var dispatcher = $"dispatcher-{suffix}@dalan.test";
    await Account(dispatcher, "Dispatcher");
    var vehicles = new List<Vehicle>(); var emails = new List<string>();
    for (var i = 0; i < 2; i++)
    {
        var vehicle = Vehicle.Register($"E2E-{suffix}-{i}", "Van", 100);
        var driver = Driver.Register($"E2E Driver {i + 1}", "09123456789", $"E2E-{suffix}-{i}",
            DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        driver.AssignVehicle(vehicle.Id);
        var email = $"driver{i + 1}-{suffix}@dalan.test";
        var user = await Account(email, "Driver");
        db.AddRange(vehicle, driver, new DriverAccount { DriverId = driver.Id, UserId = user.Id });
        vehicles.Add(vehicle); emails.Add(email);
    }
    var batches = new List<Guid[]>();
    for (var batch = 1; batch <= 2; batch++)
    {
        var deliveries = Enumerable.Range(1, 6).Select(stop => Delivery.Create($"E2E batch {batch} stop {stop}",
            "E2E warehouse", 14.6, 121.03, $"E2E destination {stop}", 14.6 + stop * .001, 121.03 + stop * .001,
            DateTimeOffset.UtcNow.AddMinutes(-5), 2)).ToArray();
        db.Deliveries.AddRange(deliveries); batches.Add(deliveries.Select(x => x.Id).ToArray());
    }
    await db.SaveChangesAsync();
    var manifest = new Fixture(dispatcher, emails.ToArray(), password, vehicles.Select(x => x.Id).ToArray(),
        batches.ToArray(), new(14.6, 121.03));
    await using (var stream = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(manifestPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await JsonSerializer.SerializeAsync(stream, manifest, jsonOptions);
    }
    Console.WriteLine("Seeded two drivers and twelve pending deliveries in the isolated E2E database.");

    async Task<IdentityUser> Account(string email, string role)
    {
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        Require(await users.CreateAsync(user, password)); Require(await users.AddToRoleAsync(user, role));
        return user;
    }
}
else if (args is ["verify"])
{
    var manifest = JsonSerializer.Deserialize<Fixture>(await File.ReadAllTextAsync(manifestPath), jsonOptions)!;
    var ids = manifest.DeliveryBatches.SelectMany(x => x).ToArray();
    var deliveries = await db.Deliveries.AsNoTracking().Where(x => ids.Contains(x.Id)).ToArrayAsync();
    var delivered = deliveries.Count(x => x.Status == DeliveryStatus.Delivered);
    var picked = deliveries.Count(x => x.Status == DeliveryStatus.PickedUp);
    var receipts = await db.DriverEventReceipts.Where(x => ids.Contains(x.DeliveryId)).ToArrayAsync();
    if (delivered != 2 || picked != 1 || receipts.Length != 5 || receipts.Count(x => x.Photo != null) != 2
        || await db.DispatchPlans.CountAsync() != 2 || await db.DispatchRoutes.CountAsync() != 4)
        throw new InvalidOperationException("Mobile E2E verification failed: expected two plans/four routes, two delivered, one picked up, five receipts and two photos.");
    Console.WriteLine("Verified: 2 plans, 4 driver routes, 12 deliveries, 2 completed proofs, 1 additional pickup and exactly 5 event receipts after replay.");
}
else throw new ArgumentException("Use seed or verify.");

static void Require(IdentityResult result)
{
    if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Code)));
}

public sealed record Fixture(string DispatcherEmail, string[] DriverEmails, string Password,
    Guid[] VehicleIds, Guid[][] DeliveryBatches, CoordinateRequest Warehouse);
