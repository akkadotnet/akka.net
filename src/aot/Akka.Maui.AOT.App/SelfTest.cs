//-----------------------------------------------------------------------
// <copyright file="SelfTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Akka.Maui.AOT.App.Actors;
using Microsoft.Extensions.DependencyInjection;

namespace Akka.Maui.AOT.App;

/// <summary>
/// The CI run. Starts on the UI thread once the page is loaded, drives <see cref="UpdateCount"/> updates from the
/// ticker actor to the screen, checks that the bound label really changed that many times, shuts the ActorSystem
/// down, prints the OK line and quits. Any failure prints FAILED and exits non-zero.
/// </summary>
internal static class SelfTest
{
    private const int UpdateCount = 25;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan UpdatesTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MarkerTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TerminateTimeout = TimeSpan.FromSeconds(30);

    public static async Task RunAsync(MainPage page, IActorRef ticker)
    {
        try
        {
            Require(MainThread.IsMainThread, "the self-test must start on the UI thread");
            var services = page.Services;
            var system = services.GetRequiredService<ActorSystem>();
            CanaryRun.Print($"page loaded; ActorSystem '{system.Name}' is up");

            CheckStartup(services);
            CheckMauiGuard();
            CanaryRun.Logs.ThrowIfAnyProblems("startup");

            await CheckUiUpdatesAsync(page, ticker);
            await CheckMetricsHopAsync(services);
            await CheckLogsAsync(system, "UI updates");

            await ShutdownAsync(system);

            CanaryRun.Print("OK");
            CanaryRun.QuitAfterSuccess();
        }
        catch (Exception ex)
        {
            CanaryRun.Fail("self-test", ex);
        }
    }

    /// <summary>AddAkkaMaui must have run every WithActors callback before MAUI built the first page.</summary>
    private static void CheckStartup(IServiceProvider services)
    {
        var registry = services.GetRequiredService<ActorRegistry>();
        Require(registry.TryGet<TickerActor>(out _), "TickerActor is not registered - AddAkkaMaui did not start the ActorSystem before the UI");
        Require(registry.TryGet<MetricsActor>(out _), "MetricsActor is not registered - AddAkkaMaui did not start the ActorSystem before the UI");
        CanaryRun.Print("startup: both actors were registered before the first page was created");
    }

    /// <summary>
    /// Plain AddAkka must refuse to run inside a MAUI app, here under Native AOT with InvariantGlobalization and a
    /// satellite assembly present (the #8782 crash). Proves the detection still sees MAUI on this runtime.
    /// </summary>
    private static void CheckMauiGuard()
    {
        CanaryRun.Print($"satellite assembly: {Strings.TryLoadSatellite()}");

        try
        {
            new ServiceCollection().AddAkka("guard-probe", _ => { });
        }
        catch (PlatformNotSupportedException)
        {
            CanaryRun.Print("guard: plain AddAkka threw PlatformNotSupportedException, as it must in a MAUI app");
            return;
        }

        throw new InvalidOperationException(
            "plain AddAkka did not throw inside a MAUI app - Akka.Hosting's MAUI detection missed MAUI on this runtime, " +
            "so AddAkka would hand this app an ActorSystem that never starts");
    }

    /// <summary>
    /// The actual point of the app: the ticker actor sends <see cref="UpdateCount"/> samples, the metrics actor pushes
    /// one snapshot per sample, and the bound label's Text must change exactly that many times, in order, on the UI
    /// thread.
    /// </summary>
    private static async Task CheckUiUpdatesAsync(MainPage page, IActorRef ticker)
    {
        var changes = page.SamplesLabelChanges;
        var before = changes.Count;
        Require(page.SamplesLabel.Text == MetricsViewModel.FormatSamples(0),
            $"before ticking the label reads '{page.SamplesLabel.Text}', not '{MetricsViewModel.FormatSamples(0)}' - the binding did not apply");

        ticker.Tell(new StartTicking(UpdateCount, TickInterval));

        var deadline = DateTime.UtcNow + UpdatesTimeout;
        while (changes.Count - before < UpdateCount)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    $"only {changes.Count - before} of {UpdateCount} label updates arrived within {UpdatesTimeout.TotalSeconds:F0}s");

            // yields the UI thread, so the queued updates can run
            await Task.Delay(25);
        }

        // nothing after the last sample: one actor message is one UI change, no more
        await Task.Delay(TickInterval * 10);
        Require(MainThread.IsMainThread, "the self-test lost the UI thread");

        var observed = changes.Skip(before).ToList();
        var expected = Enumerable.Range(1, UpdateCount).Select(MetricsViewModel.FormatSamples).ToList();
        Require(observed.Select(c => c.Text).SequenceEqual(expected),
            $"label changes were [{string.Join(", ", observed.Select(c => c.Text))}], expected '{expected[0]}' .. '{expected[^1]}' in order");
        Require(observed.All(c => c.OnUiThread), "the label changed off the UI thread");
        Require(page.ViewModel.OffUiThreadChanges == 0,
            $"the view model raised {page.ViewModel.OffUiThreadChanges} property change(s) off the UI thread");

        CanaryRun.Print($"ui: the bound label changed {observed.Count} times on the UI thread, '{expected[0]}' .. '{expected[^1]}'");
    }

    /// <summary>Every sample went through the TickerActor -> MetricsActor hop exactly once.</summary>
    private static async Task CheckMetricsHopAsync(IServiceProvider services)
    {
        var metrics = await services.GetRequiredService<IRequiredActor<MetricsActor>>().GetAsync();
        var count = await metrics.Ask<SampleCount>(GetSampleCount.Instance, AskTimeout);
        Require(count.Count == UpdateCount, $"MetricsActor saw {count.Count} samples, expected {UpdateCount}");
        CanaryRun.Print($"actors: MetricsActor received all {count.Count} samples from TickerActor");
    }

    private static async Task CheckLogsAsync(ActorSystem system, string phase)
    {
        var marker = $"{CanaryRun.Prefix} marker: {phase}";
        var seen = CanaryRun.Logs.ExpectMarker(marker);
        system.Log.Info(marker);
        if (await Task.WhenAny(seen, Task.Delay(MarkerTimeout)) != seen)
            throw new TimeoutException($"Akka's log line '{marker}' never reached Microsoft.Extensions.Logging within {MarkerTimeout.TotalSeconds:F0}s");

        CanaryRun.Logs.ThrowIfAnyProblems(phase);
    }

    /// <summary>
    /// What an app should do before it exits: run CoordinatedShutdown. MAUI never calls IHostedService.StopAsync.
    /// </summary>
    private static async Task ShutdownAsync(ActorSystem system)
    {
        var shutdown = CoordinatedShutdown.Get(system).Run(CoordinatedShutdown.ClrExitReason.Instance);
        if (await Task.WhenAny(system.WhenTerminated, Task.Delay(TerminateTimeout)) != system.WhenTerminated)
            throw new TimeoutException($"the ActorSystem did not terminate within {TerminateTimeout.TotalSeconds:F0}s of CoordinatedShutdown");

        await shutdown;
        CanaryRun.Logs.ThrowIfAnyProblems("shutdown");
        CanaryRun.Print("shutdown: CoordinatedShutdown terminated the ActorSystem");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
