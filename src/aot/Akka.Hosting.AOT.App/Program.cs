//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.DependencyInjection;
using Akka.Event;
using Akka.Hosting;
using Akka.Hosting.AOT.App;
using Akka.Hosting.AOT.App.Actors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const string RoundTripMarker = "[canary-hosting] round-trip complete";

var askTimeout = TimeSpan.FromSeconds(5);
var stopTimeout = TimeSpan.FromSeconds(30);
var markerTimeout = TimeSpan.FromSeconds(10);

// a crash on a pool thread would otherwise kill the process with no diagnosis at all
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    PrintFailure(e.ExceptionObject as Exception);
    Console.Out.Flush();
};

// This watchdog plays the same role Akka.AOT.App's LogWatchdogFilter plays for the plain-core
// canary: any Warning/Error logged - by Akka through LoggerFactoryLogger, or by the host/config
// system directly - fails the run. See WatchdogLoggerProvider.cs.
var watchdog = new WatchdogLoggerProvider(RoundTripMarker);

var appBuilder = Host.CreateApplicationBuilder(args);

// Replace the default console provider with just the watchdog: the point is to fail on
// Warning/Error, not to duplicate console output the canary already writes itself.
appBuilder.Logging.ClearProviders();
appBuilder.Logging.AddProvider(watchdog);
appBuilder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);

// Dependency injection (Akka.DependencyInjection): plain services, resolved into an actor's
// constructor through resolver.Props<T>() below.
appBuilder.Services.AddSingleton<IGreetingService, GreetingService>();

// Health checks: a typical Hosting app wires Microsoft.Extensions.Diagnostics.HealthChecks in and
// adds Akka's built-in liveness check to it with WithActorSystemLivenessCheck() below.
appBuilder.Services.AddHealthChecks();

appBuilder.Services.AddAkka("hosting-aot", (builder, _) =>
{
    builder
        // WithExtension<T>: a custom Akka.NET extension, started automatically at boot through
        // ExtensionsSetup (see CanaryExtension.cs).
        .WithExtension<CanaryExtensionProvider>()

        // WithActorSystemLivenessCheck: the built-in health check every typical Hosting app wires
        // into Microsoft.Extensions.Diagnostics.HealthChecks - unhealthy once the ActorSystem
        // terminates.
        .WithActorSystemLivenessCheck()

        // ConfigureLoggers: Hosting's idiomatic default - route Akka log events through the
        // Microsoft.Extensions.Logging ILoggerFactory (LoggerFactoryLogger) instead of Akka's own
        // stdout logger, so they land on the same ILoggerProvider pipeline as everything else,
        // including the watchdog above. This is also the path #8654 covers: LoggerFactoryLogger's
        // assembly-qualified name has to resolve out of akka.loggers with Akka.DynamicTypeLoading
        // off.
        .ConfigureLoggers(logger =>
        {
            logger.ClearLoggers();
            logger.AddLoggerFactory();
            logger.LogLevel = Akka.Event.LogLevel.InfoLevel;
        })

        // WithActors((system, registry, resolver) => ...): register plain actors in the
        // ActorRegistry, and ones built through Akka.DependencyInjection's resolver.Props<T>().
        .WithActors((system, registry, resolver) =>
        {
            var echo = system.ActorOf(Props.Create(() => new EchoActor()), "echo-actor");
            registry.Register<EchoActor>(echo);

            var diGreeter = system.ActorOf(resolver.Props<DiGreeterActor>(), "di-greeter-actor");
            registry.Register<DiGreeterActor>(diGreeter);

            // DeathWatch: a dedicated watcher, proven against a short-lived target actor in
            // AssertDeathWatchAsync below.
            var watcher = system.ActorOf(Props.Create(() => new WatcherActor()), "watcher-actor");
            registry.Register<WatcherActor>(watcher);
        })

        // AddStartup: runs exactly once, after every actor above has been instantiated. Used here
        // to mark the extension instance so Program can prove WithExtension<T> resolved the same
        // one, not a second instance created some other way.
        .AddStartup((system, _) =>
        {
            // system.WithExtension<T, TI>() below resolves-OR-CREATES: if the boot-time
            // registration never happened, it would silently create a fresh instance and this
            // canary would mark and later find that same fresh instance, proving nothing.
            // HasExtension<T>() only reads, so it is what actually proves Hosting registered the
            // extension at boot.
            Require(system.HasExtension<CanaryExtension>(),
                "CanaryExtension was not registered by boot - Hosting's WithExtension<CanaryExtensionProvider>() did not run");

            var ext = system.WithExtension<CanaryExtension, CanaryExtensionProvider>();
            ext.Marked = true;
        });
});

var host = appBuilder.Build();
await host.StartAsync();

try
{
    watchdog.ThrowIfAnyProblems("startup");

    var system = host.Services.GetRequiredService<ActorSystem>();
    system.Log.Info("[canary-hosting] actor system up");

    AssertExtension(system);
    await AssertRegistryAndDiAsync(host.Services);
    await AssertDeathWatchAsync(system, host.Services);
    await AssertHealthAsync(host.Services);

    system.Log.Info(RoundTripMarker);

    // Proves fix for the race between Log.Info above and the watchdog's queue: the logger actor
    // delivers in order, so seeing this marker means every earlier warning/error has already been
    // seen too, and it independently proves Akka's logs actually reach Microsoft.Extensions.Logging.
    await watchdog.WaitForMarkerAsync(markerTimeout);
    watchdog.ThrowIfAnyProblems("round-trip");

    Console.WriteLine("[canary-hosting] OK");

    // Self-terminate rather than wait on host.WaitForShutdownAsync() for an external SIGINT/SIGTERM:
    // CI needs a deterministic exit, and a process that has already proven every assertion has
    // nothing left to wait for. The finally block below stops the host the same way either way.
    return 0;
}
catch (Exception ex)
{
    PrintFailure(ex);
    return 1;
}
finally
{
    using var cts = new CancellationTokenSource(stopTimeout);
    await host.StopAsync(cts.Token);
    Console.WriteLine("[canary-hosting] host stopped");
}

void AssertExtension(ActorSystem system)
{
    var ext = system.WithExtension<CanaryExtension, CanaryExtensionProvider>();
    Require(ext.Marked, "WithExtension<CanaryExtensionProvider> did not resolve the instance AddStartup marked");
}

async Task AssertRegistryAndDiAsync(IServiceProvider services)
{
    var echoRequired = services.GetRequiredService<IRequiredActor<EchoActor>>();
    var echoRef = await echoRequired.GetAsync();
    var echoReply = await echoRef.Ask<string>("ping", askTimeout);
    Require(echoReply == "echo:ping", $"echo actor replied '{echoReply}'");

    var diRequired = services.GetRequiredService<IRequiredActor<DiGreeterActor>>();
    var diRef = await diRequired.GetAsync();
    var diReply = await diRef.Ask<string>("world", askTimeout);
    Require(diReply == "hello, world", $"DI-injected actor replied '{diReply}'");
}

async Task AssertDeathWatchAsync(ActorSystem system, IServiceProvider services)
{
    var watcherRequired = services.GetRequiredService<IRequiredActor<WatcherActor>>();
    var watcherRef = await watcherRequired.GetAsync();

    var target = system.ActorOf(Props.Create(() => new EchoActor()), "watch-target");
    var watching = await watcherRef.Ask<string>(target, askTimeout);
    Require(watching == "watching", $"watcher replied '{watching}' to the Watch request");

    target.Tell(PoisonPill.Instance);

    var terminated = await watcherRef.Ask<string>("terminated?", askTimeout);
    Require(terminated == "terminated:watch-target", $"watcher replied '{terminated}' after DeathWatch");
}

async Task AssertHealthAsync(IServiceProvider services)
{
    var healthCheckService = services.GetRequiredService<HealthCheckService>();
    var report = await healthCheckService.CheckHealthAsync();
    Require(report.Status == HealthStatus.Healthy, $"health check report status was '{report.Status}'");
    Require(report.Entries.ContainsKey("akka.actorsystem"),
        "report had no 'akka.actorsystem' entry - WithActorSystemLivenessCheck() never registered its check");
}

void Require(bool condition, string problem)
{
    if (!condition)
        throw new InvalidOperationException(problem);
}

void PrintFailure(Exception? ex)
{
    if (ex is null)
    {
        Console.WriteLine("[canary-hosting] FAILED: unhandled non-exception throw");
        return;
    }

    Console.WriteLine("[canary-hosting] FAILED:");
    Console.WriteLine(ex);
}
