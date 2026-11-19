var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Reserved host: no external API endpoints are enabled.
app.Run();
