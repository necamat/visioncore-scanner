using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VisionCore.Console;

using var host = AppHost.Build(args);

// Starting the host triggers options ValidateOnStart, so invalid configuration
// fails fast before any processing begins.
await host.StartAsync();
try
{
    // ApplicationStopping fires on Ctrl+C (SIGINT) and SIGTERM, so a cancelled
    // run unwinds through the pipeline's cooperative cancellation checks
    // instead of being killed mid-write.
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    var orchestrator = host.Services.GetRequiredService<ConsoleOrchestrator>();
    return await orchestrator.RunAsync(args, lifetime.ApplicationStopping);
}
catch (OperationCanceledException)
{
    return 130; // Conventional exit code for termination by Ctrl+C.
}
finally
{
    await host.StopAsync();
}
