using Dalan.Application.Vehicles;
using Dalan.Infrastructure.Persistence;
using Dalan.Infrastructure.Repositories;
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
        return services;
    }
}
