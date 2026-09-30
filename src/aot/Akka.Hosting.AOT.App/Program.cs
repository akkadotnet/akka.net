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

var askTimeout = TimeSpan.FromSeconds(5);
var stopTimeout = TimeSpan.FromSeconds(30);

// a crash on a pool thread would otherwise kill the process with no diagnosis at all
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    PrintFailure(e.ExceptionObject as Exception);
    Console.Out.Flush();
};

// This watchdog plays the same role Akka.AOT.App's LogWatchdogFilter plays for the plain-core
// canary: any Warning/Error logged - by Akka through LoggerFactoryLogger, or by the host/config
// system directly - fails the run. See WatchdogLoggerProvider.cs.
var watchdog = new WatchdogLoggerProvider();

var appBuilder = Host.CreateApplicationBuilder(args);

// Replace the default console provider with just the watchdog: the point is to fail on
// Warning/Error, not to duplicate console output the canary already writes itself.
appBuilder.Logging.ClearProviders();
appBuilder.Logging.AddProvider(watchdog);
appBuilder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);

// Dependency injection (Akka.DependencyInjection): plain services, resolved into an actor's
// constructor through resolver.Props<T>() below.
appBuilder.Services.AddSingleton<IGreetingService, GreetingService>();
appBuilder.Services.AddSingleton<INotificationSink, NotificationSink>();

// Health checks: a typical Hosting app wires Microsoft.Extensions.Diagnostics.HealthChecks in and
// adds Akka's built-in liveness check to it with WithActorSystemLivenessCheck() below.
appBuilder.Services.AddHealthChecks();

appBuilder.Services.AddAkka("hosting-aot", (builder, _) =>
{
    builder
        // AddHocon: plain settings merged ahead of akka.conf.
        .AddHocon("""
                  canary {
                    plain-setting = 42
                    nested.value = "hi"
                  }
                  """, HoconAddMode.Prepend)

        // WithExtension<T>: a custom Akka.NET extension, started automatically at boot.
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

            // Tell: a pure fire-and-forget actor, never Ask'd - the counterpart to echo/di-greeter
            // above, which both round-trip through Ask.
            var notifier = system.ActorOf(resolver.Props<NotifyActor>(), "notify-actor");
            registry.Register<NotifyActor>(notifier);

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

    AssertHocon(system);
    await AssertExtensionAsync(system);
    await AssertRegistryAndDiAsync(host.Services);
    await AssertTellAsync(host.Services);
    await AssertDeathWatchAsync(system, host.Services);
    await AssertHealthAsync(host.Services);

    system.Log.Info("[canary-hosting] round-trip complete");
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

void AssertHocon(ActorSystem system)
{
    Require(system.Settings.Config.GetInt("canary.plain-setting") == 42,
        $"canary.plain-setting resolved to [{system.Settings.Config.GetInt("canary.plain-setting")}]");
    Require(system.Settings.Config.GetString("canary.nested.value") == "hi",
        $"canary.nested.value resolved to [{system.Settings.Config.GetString("canary.nested.value")}]");
}

async Task AssertExtensionAsync(ActorSystem system)
{
    // WithExtension<T> writes CanaryExtensionProvider's AssemblyQualifiedName into the
    // akka.extensions HOCON list; ActorSystemImpl.LoadExtensions() resolves it back with
    // Type.GetType(...) + Activator.CreateInstance(...) at boot. system.WithExtension<...>() here
    // resolves (or lazily creates) the extension through the normal keyed-extension cache - if the
    // boot-time load actually ran, this returns the SAME marked instance instead of a fresh one.
    var ext = system.WithExtension<CanaryExtension, CanaryExtensionProvider>();
    Require(ext.Marked, "WithExtension<CanaryExtensionProvider> did not resolve the instance AddStartup marked " +
                         "- either the akka.extensions load never ran, or it created a second instance");
    await Task.CompletedTask;
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

async Task AssertTellAsync(IServiceProvider services)
{
    // Tell: fire-and-forget, no reply expected - the counterpart to the Ask round-trips above.
    var notifyRequired = services.GetRequiredService<IRequiredActor<NotifyActor>>();
    var notifyRef = await notifyRequired.GetAsync();
    var sink = services.GetRequiredService<INotificationSink>();

    notifyRef.Tell("fire-and-forget");

    var received = await sink.Received.WaitAsync(askTimeout);
    Require(received == "fire-and-forget", $"notify sink received '{received}'");
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

    Console.WriteLine($"[canary-hosting] FAILED: {ex.GetType().FullName}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);

    var inner = ex.InnerException;
    var depth = 0;
    while (inner is not null && depth++ < 10)
    {
        Console.WriteLine($"[canary-hosting]  --> inner: {inner.GetType().FullName}: {inner.Message}");
        Console.WriteLine(inner.StackTrace);
        inner = inner.InnerException;
    }
}
