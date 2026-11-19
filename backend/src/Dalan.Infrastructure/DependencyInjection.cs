using Dalan.Application.DriverExecution;
using Dalan.Application.Deliveries;
using Dalan.Application.Dispatch;
using Dalan.Application.Drivers;
using Dalan.Application.Routing;
using Dalan.Application.Vehicles;
using Dalan.Infrastructure.Persistence;
using Dalan.Infrastructure.Repositories;
using Dalan.Infrastructure.Routing;
using Dalan.Application.Notifications;
using Dalan.Infrastructure.Notifications;
using Dalan.Application.Tracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
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
        services.AddScoped<Dalan.Application.Identity.IDriverAccountsRepository, DriverAccountsRepository>();
        services.AddScoped<Dalan.Application.Identity.IDriverAccountsService, Dalan.Application.Identity.DriverAccountsService>();
        services.AddScoped<Dalan.Application.Identity.IDriverInvitationsRepository, DriverInvitationsRepository>();
        services.AddScoped<Dalan.Application.Identity.IDriverInvitationsService, Dalan.Application.Identity.DriverInvitationsService>();
        services.AddScoped<Dalan.Application.Identity.IDriverRecoveryRepository, DriverRecoveryRepository>();
        services.AddScoped<Dalan.Application.Identity.IDriverRecoveryService, Dalan.Application.Identity.DriverRecoveryService>();
        services.AddScoped<Dalan.Application.Identity.IDriverAccessRepository, DriverAccessRepository>();
        services.AddScoped<Dalan.Application.Identity.IDriverAccessService, Dalan.Application.Identity.DriverAccessService>();
        services.AddScoped<Dalan.Application.Identity.IAccountAuditRepository, AccountAuditRepository>();
        services.AddScoped<Dalan.Application.Identity.IAccountAuditService, Dalan.Application.Identity.AccountAuditService>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<IDriverRepository, DriverRepository>();
        services.AddScoped<DriverTasksRepository>();
        services.AddScoped<IDriverTasks>(sp => sp.GetRequiredService<DriverTasksRepository>());
        services.AddScoped<IGpsExceptionRepository, GpsExceptionRepository>();
        services.AddScoped<IGpsExceptionService, GpsExceptionService>();
        services.AddScoped<IDriverNavigationService, DriverNavigationService>();
        services.AddScoped<IDeliveryRepository, DeliveryRepository>();
        services.AddScoped<IDispatchRepository, DispatchRepository>();
        services.AddScoped<Dalan.Application.Analytics.IAnalyticsRepository, AnalyticsRepository>();
        services.AddSingleton(new TrackingOptions { Enabled = bool.TryParse(configuration["Tracking:Enabled"], out var trackingEnabled) && trackingEnabled });
        services.AddScoped<IFleetTrackingRepository, FleetTrackingRepository>();
        services.AddScoped<IFleetTrackingService, FleetTrackingService>();
        var email = new ResendOptions
        {
            Enabled = bool.TryParse(configuration["Resend:Enabled"], out var sendEnabled) && sendEnabled,
            ApiKey = configuration["Resend:ApiKey"] ?? "",
            From = configuration["Resend:From"] ?? "",
            Recipients = configuration["Resend:Recipients"] ?? ""
        };
        if (email.Enabled && (string.IsNullOrWhiteSpace(email.ApiKey)
            || !System.Net.Mail.MailAddress.TryCreate(email.From, out _)
            || email.Recipients.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is not { Length: >= 1 and <= 50 } recipients
            || recipients.Any(address => !System.Net.Mail.MailAddress.TryCreate(address, out _))))
            throw new InvalidOperationException("Enabled Resend notifications require an API key, a valid sender and 1–50 valid recipient addresses.");
        services.AddSingleton(email);
        var credentialEnabled = bool.TryParse(configuration["DriverCredentialEmail:Enabled"], out var credentialsOn) && credentialsOn;
        var keyRingPath = configuration["DriverCredentialEmail:KeyRingPath"] ?? "";
        if (credentialEnabled && (!email.Enabled || string.IsNullOrWhiteSpace(keyRingPath)
            || !Path.IsPathFullyQualified(keyRingPath)))
            throw new InvalidOperationException(
                "Driver credential email requires Resend and a shared absolute DriverCredentialEmail:KeyRingPath.");
        var protection = services.AddDataProtection().SetApplicationName("Dalan.DriverCredentialEmail");
        if (credentialEnabled)
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        services.AddSingleton(new DriverCredentialEmailOptions(credentialEnabled, keyRingPath));
        services.AddScoped<IDriverCredentialEmailOutbox, DriverCredentialEmailOutbox>();
        services.AddHttpClient<ResendEmailSender>(client => { client.BaseAddress = new Uri("https://api.resend.com/"); client.Timeout = TimeSpan.FromSeconds(15); })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<IEmailSender, OutboxRoutedEmailSender>();
        services.AddScoped<IOperationsNotificationRepository, OperationsNotificationRepository>();
        services.AddScoped<IOperationsNotificationService, OperationsNotificationService>();
        var googleKey = configuration["GooglePlaces:ApiKey"];
        if (!string.IsNullOrWhiteSpace(googleKey))
            services.AddHttpClient("GooglePlaces", client =>
            {
                client.BaseAddress = new Uri("https://places.googleapis.com/");
                client.Timeout = TimeSpan.FromSeconds(10);
            }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
                .AddTypedClient<IGooglePlacesSearch>((client, _) => new GooglePlacesTextSearch(client, googleKey));
        if (!string.IsNullOrWhiteSpace(googleKey))
            services.AddScoped<IGoogleAddressSearch>(sp => new GoogleAddressSearch(sp.GetRequiredService<IHttpClientFactory>().CreateClient("GooglePlaces"), googleKey));
        var photonUrl = configuration["Geocoding:PhotonBaseUrl"];
        if (!string.IsNullOrWhiteSpace(photonUrl))
        {
            if (!Uri.TryCreate(photonUrl, UriKind.Absolute, out var endpoint)
                || endpoint.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Geocoding:PhotonBaseUrl must be an absolute HTTP(S) URL.");
            services.AddHttpClient<IPlaceSearchService, PhotonPlaceSearchService>(client =>
            {
                client.BaseAddress = new Uri(endpoint.ToString().TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(8);
            });
        }
        var valhallaUrl = configuration["Valhalla:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(valhallaUrl))
        {
            if (!Uri.TryCreate(valhallaUrl, UriKind.Absolute, out var endpoint)
                || endpoint.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Valhalla:BaseUrl must be an absolute HTTP(S) URL.");

            var maxCellsValue = configuration["Valhalla:MaxMatrixCells"];
            var maxCells = 100;
            if (maxCellsValue is not null && (!int.TryParse(maxCellsValue, out maxCells) || maxCells is < 1 or > 10000))
                throw new InvalidOperationException("Valhalla:MaxMatrixCells must be 1–10000.");
            services.AddSingleton(new ValhallaMatrixLimits(maxCells));

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
