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


Log.Information("Starting server...");

var builder = Host.CreateEmptyApplicationBuilder(settings: null);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
builder.Logging.AddSerilog(Log.Logger);

var path = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? throw new Exception("Unable to determine the path of the assembly.");
var configurationBuilder = new ConfigurationBuilder()
    .SetBasePath(path)
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables();
var configuration = configurationBuilder.Build();

builder.Configuration.AddConfiguration(configuration);
builder.Services.AddSingleton<AzdoService>();

await builder.Build().RunAsync();