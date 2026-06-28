using Microsoft.Extensions.Configuration;

namespace CryptoIndicatorApp.Desktop.Configuration;

public static class DashboardConfiguration
{
    public static DashboardOptions Load(string? basePath = null)
    {
        var resolvedBasePath = basePath ?? AppContext.BaseDirectory;
        var configuration = new ConfigurationBuilder()
            .SetBasePath(resolvedBasePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var options = new DashboardOptions();
        configuration.GetSection("Dashboard").Bind(options);
        options.Normalize();
        return options;
    }
}
