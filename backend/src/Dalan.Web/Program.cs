using Dalan.Application.Vehicles;
using Dalan.Application.Drivers;
using Dalan.Application.Deliveries;
using Dalan.Application.FleetOptimization;
using Dalan.Application.Dispatch;
using Dalan.Infrastructure;
using Dalan.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddDalanDevelopmentEnvironment(builder.Environment.EnvironmentName, builder.Environment.ContentRootPath);

// Add services to the container.
builder.Services.AddControllersWithViews();
// Conservative per-instance throttle on the public Driver recovery endpoints.
// Keep the feature disabled until the recovery UI/email integration is verified.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("driver-recovery", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(15);
        limiter.QueueLimit = 0;
    });
});
builder.Services.AddScoped<Dalan.Application.Analytics.IAnalyticsService, Dalan.Application.Analytics.AnalyticsService>();
builder.Services.Configure<Dalan.Web.Models.PlanningMapOptions>(builder.Configuration.GetSection("PlanningMap"));
builder.Services.AddScoped<IVehicleService, VehicleService>();
builder.Services.AddScoped<IDriverService, DriverService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IDeliveryService, DeliveryService>();
builder.Services.AddDalanInfrastructure(builder.Configuration);
builder.Services.AddScoped<IDispatchPlanReader, DispatchPlanReader>();
if (!string.IsNullOrWhiteSpace(builder.Configuration["Valhalla:BaseUrl"]))
{
    builder.Services.AddScoped<IFleetOptimizer, FleetOptimizer>();
    builder.Services.AddScoped<IDispatchService, DispatchService>();
}
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<DalanDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Denied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

var app = builder.Build();

var initialAdminPassword = builder.Configuration["DALAN_ADMIN_PASSWORD"];
if (!string.IsNullOrWhiteSpace(initialAdminPassword))
{
    const string email = "admin@dalan.local";

    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<DalanDbContext>();
    if ((await context.Database.GetPendingMigrationsAsync()).Any())
        throw new InvalidOperationException("Apply database migrations explicitly before starting with DALAN_ADMIN_PASSWORD.");

    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var name in new[] { "Admin", "Dispatcher" })
    {
        if (!await roles.RoleExistsAsync(name))
        {
            var roleResult = await roles.CreateAsync(new IdentityRole(name));
            if (!roleResult.Succeeded)
                throw new InvalidOperationException(string.Join("; ", roleResult.Errors.Select(error => error.Description)));
        }
    }

    var existing = await users.FindByEmailAsync(email);
    if (existing is null)
    {
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var createResult = await users.CreateAsync(user, initialAdminPassword);
        if (!createResult.Succeeded)
            throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(error => error.Description)));

        var assignment = await users.AddToRoleAsync(user, "Admin");
        if (!assignment.Succeeded)
            throw new InvalidOperationException(string.Join("; ", assignment.Errors.Select(error => error.Description)));

        app.Logger.LogInformation("Initial Admin account created. Remove DALAN_ADMIN_PASSWORD from bootstrap configuration when no longer needed.");
    }
    else if (!await users.IsInRoleAsync(existing, "Admin"))
    {
        throw new InvalidOperationException("The fixed Admin email exists without the Admin role; refusing to elevate it.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
