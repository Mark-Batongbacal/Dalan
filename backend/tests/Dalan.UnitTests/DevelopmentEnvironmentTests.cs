using Dalan.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dalan.UnitTests;

public sealed class DevelopmentEnvironmentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DalanEnvironmentTests_" + Guid.NewGuid().ToString("N"));

    public DevelopmentEnvironmentTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "backend", "src", "Host"));
        File.WriteAllText(Path.Combine(root, "backend", "Dalan.sln"), "");
        File.WriteAllText(Path.Combine(root, ".env"), "ConnectionStrings__Dalan=\"Server=test;Database=fixture\"\nValhalla__BaseUrl=https://routing.test/\nDALAN_DOTENV_PROBE=value");
    }

    [Fact]
    public void Repository_root_file_loads_from_host_directory_without_mutating_process_environment()
    {
        var original = Environment.GetEnvironmentVariable("DALAN_DOTENV_PROBE");
        var config = new ConfigurationManager();
        config.AddDalanDevelopmentEnvironment("Development", Path.Combine(root, "backend", "src", "Host"));
        Assert.Equal("Server=test;Database=fixture", config.GetConnectionString("Dalan"));
        Assert.Equal("https://routing.test/", config["Valhalla:BaseUrl"]);
        Assert.Equal(original, Environment.GetEnvironmentVariable("DALAN_DOTENV_PROBE"));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public void Non_development_hosts_do_not_read_local_configuration(string environment)
    {
        var config = new ConfigurationManager();
        config.AddDalanDevelopmentEnvironment(environment, root);
        Assert.Null(config.GetConnectionString("Dalan"));
    }

    [Fact]
    public void Explicit_host_configuration_overrides_local_file_and_missing_file_is_optional()
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Dalan"] = "explicit-test-database" });
        config.AddDalanDevelopmentEnvironment("Development", root);
        Assert.Equal("explicit-test-database", config.GetConnectionString("Dalan"));
        File.Delete(Path.Combine(root, ".env"));
        var empty = new ConfigurationManager();
        empty.AddDalanDevelopmentEnvironment("Development", root);
        Assert.Null(empty.GetConnectionString("Dalan"));
    }

    [Fact]
    public void Verification_file_overrides_base_file_but_not_explicit_configuration()
    {
        File.WriteAllText(Path.Combine(root, ".env.verify"), "DALAN_DOTENV_PROBE=verification\nDALAN_ADMIN_PASSWORD=verification-fixture-only");
        var original = Environment.GetEnvironmentVariable("DALAN_ADMIN_PASSWORD");
        var config = new ConfigurationManager();
        config.AddDalanDevelopmentEnvironment("Development", root);
        Assert.Equal("verification", config["DALAN_DOTENV_PROBE"]);
        Assert.Equal("verification-fixture-only", config["DALAN_ADMIN_PASSWORD"]);
        Assert.Equal(original, Environment.GetEnvironmentVariable("DALAN_ADMIN_PASSWORD"));
        var explicitConfig = new ConfigurationManager();
        explicitConfig.AddInMemoryCollection(new Dictionary<string, string?> { ["DALAN_ADMIN_PASSWORD"] = "explicit-fixture-only" });
        explicitConfig.AddDalanDevelopmentEnvironment("Development", root);
        Assert.Equal("explicit-fixture-only", explicitConfig["DALAN_ADMIN_PASSWORD"]);
    }

    public void Dispose()
    {
        var full = Path.GetFullPath(root);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(full).StartsWith("DalanEnvironmentTests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected fixture directory.");
        Directory.Delete(full, recursive: true);
    }
}
