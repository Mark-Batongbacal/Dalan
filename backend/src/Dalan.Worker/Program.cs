using Dalan.Infrastructure;
using Dalan.Worker;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
if (!builder.Configuration.GetValue<bool>("Operations:Enabled"))
{
    Console.WriteLine("Operations jobs are disabled. Set Operations__Enabled=true to run the worker.");
    return;
}

var storage = builder.Configuration.GetConnectionString("Hangfire");
if (string.IsNullOrWhiteSpace(storage))
    throw new InvalidOperationException("Configure ConnectionStrings:Hangfire for the worker.");

builder.Services.AddDalanInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(storage, new SqlServerStorageOptions
    {
        PrepareSchemaIfNecessary = builder.Configuration.GetValue<bool>("Operations:PrepareHangfireSchema")
    }));
builder.Services.AddHangfireServer();
builder.Services.AddScoped<OperationsJobs>();

using var host = builder.Build();
var jobs = host.Services.GetRequiredService<IRecurringJobManager>();
jobs.AddOrUpdate<OperationsJobs>("overdue-deliveries",
    job => job.RunAsync("overdue", null, CancellationToken.None), "0 * * * *");
jobs.AddOrUpdate<OperationsJobs>("dispatch-summary",
    job => job.RunAsync("dispatch", null, CancellationToken.None), "5 0 * * *");
jobs.AddOrUpdate<OperationsJobs>("maintenance-reminders",
    job => job.RunAsync("maintenance", null, CancellationToken.None), "15 0 * * *");
await host.RunAsync();
