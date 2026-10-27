using Dalan.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dalan.IntegrationTests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly string databaseName = "DalanTests_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; }

    public SqlServerFixture()
    {
        var builder = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("DALAN_TEST_SQLSERVER")
            ?? "Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True");
        // Never use or delete the database supplied by the caller.
        builder.InitialCatalog = databaseName;
        ConnectionString = builder.ConnectionString;
    }

    public DalanDbContext CreateContext()
        => new(new DbContextOptionsBuilder<DalanDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        var catalog = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        if (catalog != databaseName || !catalog.StartsWith("DalanTests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to delete a non-test database.");

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}

[CollectionDefinition("SQL Server")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
