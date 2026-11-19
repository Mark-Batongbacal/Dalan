using DotNetEnv;
using DotNetEnv.Configuration;
using Microsoft.Extensions.Configuration;

namespace Dalan.Infrastructure;

public static class DevelopmentEnvironment
{
    public static void AddDalanDevelopmentEnvironment(this ConfigurationManager configuration,
        string environmentName, string contentRoot)
    {
        if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)) return;
        for (var directory = new DirectoryInfo(contentRoot); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "backend", "Dalan.sln"))) continue;
            // A local fallback: normal JSON, secrets, process environment and CLI overrides still win.
            // No process-wide variables are changed, so concurrent test hosts stay isolated.
            foreach (var name in new[] { ".env.verify", ".env" })
            {
                var path = Path.Combine(directory.FullName, name);
                if (!File.Exists(path)) continue;
                var source = new ConfigurationBuilder().AddDotNetEnv(path, LoadOptions.NoEnvVars()).Sources.Single();
                configuration.Sources.Insert(0, source);
            }
            return;
        }
    }
}
