using Dalan.Application.DriverExecution;
using Dalan.Application.Deliveries;
using Dalan.Application.Dispatch;
using Dalan.Application.Drivers;
using Dalan.Application.Routing;
using Dalan.Application.Vehicles;
using Dalan.Infrastructure.Persistence;
using Dalan.Infrastructure.Repositories;
using Dalan.Infrastructure.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dalan.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDalanInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Dalan");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Configure ConnectionStrings:Dalan through user secrets or ConnectionStrings__Dalan.");
        }

        services.AddDbContext<DalanDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<IDriverRepository, DriverRepository>();
        services.AddScoped<IDriverTasks, DriverTasksRepository>();
        services.AddScoped<IDeliveryRepository, DeliveryRepository>();
        services.AddScoped<IDispatchRepository, DispatchRepository>();
        var valhallaUrl = configuration["Valhalla:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(valhallaUrl))
        {
            if (!Uri.TryCreate(valhallaUrl, UriKind.Absolute, out var endpoint)
                || endpoint.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Valhalla:BaseUrl must be an absolute HTTP(S) URL.");

            services.AddHttpClient<IRoutingService, ValhallaRoutingService>(client =>
            {
                client.BaseAddress = new Uri(endpoint.ToString().TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(10);
            });
            services.AddHttpClient<ITravelMatrixService, ValhallaTravelMatrixService>(client =>
            {
                client.BaseAddress = new Uri(endpoint.ToString().TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(20);
            });
        }
        return services;
    }
}
