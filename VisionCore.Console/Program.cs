using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VisionCore.Console;

using var host = AppHost.Build(args);

// Starting the host triggers options ValidateOnStart, so invalid configuration
// fails fast before any processing begins.
await host.StartAsync();
try
{
    var orchestrator = host.Services.GetRequiredService<ConsoleOrchestrator>();
    return await orchestrator.RunAsync(args, CancellationToken.None);
}
finally
{
    await host.StopAsync();
}
