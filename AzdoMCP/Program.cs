using ModelContextProtocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog;
using Microsoft.Extensions.Logging;
using System.Reflection;
using MCP.Services;

Log.Logger = new LoggerConfiguration()
           .MinimumLevel.Verbose() // Capture all log levels
           .WriteTo.File(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "TestServer_.log"),
               rollingInterval: RollingInterval.Day,
               outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
           .WriteTo.Debug()
           .WriteTo.Console(standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose)
           .CreateLogger();

try
{
    Log.Information("Starting server...");

    var builder = Host.CreateEmptyApplicationBuilder(settings: null);

    builder.Logging.AddSerilog(Log.Logger);

    var path = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? throw new Exception("Unable to determine the path of the assembly.");
    var configurationBuilder = new ConfigurationBuilder()
        .SetBasePath(path)
        .AddJsonFile("appsettings.json")
        .AddEnvironmentVariables();
    var configuration = configurationBuilder.Build();
    builder.Configuration.AddConfiguration(configuration);

    builder.Services
           .AddMcpServer()
           .WithStdioServerTransport()
           .WithToolsFromAssembly();
           
    builder.Services.AddSingleton<AzdoService>();

    var app = builder.Build();

    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
