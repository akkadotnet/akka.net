//-----------------------------------------------------------------------
// <copyright file="MauiProgram.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Hosting;
using Akka.Hosting.Maui;
using Akka.Maui.AOT.App.Actors;
using Microsoft.Extensions.Logging;

namespace Akka.Maui.AOT.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        CanaryRun.Initialize();

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // Every log line - Akka's through LoggerFactoryLogger, and MAUI's own (binding failures are logged at
        // Warning) - goes through this provider, which prints it and fails a self-test run on Warning or Error.
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(CanaryRun.Logs);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services.AddSingleton<MetricsViewModel>();
        builder.Services.AddSingleton<MainPage>();

        // AddAkkaMaui, not AddAkka: MAUI never starts IHostedService instances (dotnet/maui#2244). This starts the
        // ActorSystem inside builder.Build() below, so both actors exist before the first page does.
        builder.Services.AddAkkaMaui("maui-canary", (akka, services) => akka
            .ConfigureLoggers(loggers =>
            {
                loggers.ClearLoggers();
                loggers.AddLoggerFactory();
                loggers.LogLevel = Akka.Event.LogLevel.InfoLevel;
            })
            .WithActors((system, registry) =>
            {
                // MetricsActor pushes every snapshot to the view model, which marshals it onto the UI thread.
                var metrics = system.ActorOf(
                    Props.Create(() => new MetricsActor(services.GetRequiredService<MetricsViewModel>())), "metrics");
                registry.Register<MetricsActor>(metrics);

                // TickerActor -> MetricsActor is the actor-to-actor hop between the timer and the screen.
                var ticker = system.ActorOf(Props.Create(() => new TickerActor(metrics)), "ticker");
                registry.Register<TickerActor>(ticker);
            }));

        return builder.Build();
    }
}
