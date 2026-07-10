//-----------------------------------------------------------------------
// <copyright file="ClusterStartupFuzzSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests
{
    /// <summary>
    /// Thread-safe sink that the <see cref="ClusterStartupFuzzLogger"/> uses to hand back captured
    /// "Failed to startup Cluster" error text to the test, keyed by (unique) actor-system name.
    /// </summary>
    internal static class FuzzFailureSink
    {
        private static readonly ConcurrentDictionary<string, ConcurrentQueue<string>> ByName = new();

        public static void Record(string systemName, string text)
            => ByName.GetOrAdd(systemName, _ => new ConcurrentQueue<string>()).Enqueue(text);

        public static bool TryGet(string systemName, out string text)
        {
            if (ByName.TryGetValue(systemName, out var q) && q.TryPeek(out var first))
            {
                text = first;
                return true;
            }

            text = string.Empty;
            return false;
        }

        public static void Clear(string systemName) => ByName.TryRemove(systemName, out _);
    }

    /// <summary>
    /// A minimal logger installed via <c>akka.loggers</c> so that it is subscribed to the
    /// <see cref="EventStream"/> <b>before</b> the Cluster extension is constructed (loggers are
    /// started in <c>ActorRefProvider.Init</c> prior to <c>Cluster.Get</c>). This is the only reliable
    /// way to observe the <c>"Failed to startup Cluster"</c> error that the startup race emits from the
    /// Cluster constructor <b>during</b> <see cref="ActorSystem.Create(string, Config)"/>.
    /// </summary>
    public sealed class ClusterStartupFuzzLogger : ReceiveActor, IRequiresMessageQueue<ILoggerMessageQueueSemantics>
    {
        public const string FailureMarker = "Failed to startup Cluster";

        public ClusterStartupFuzzLogger()
        {
            Receive<InitializeLogger>(_ => Sender.Tell(new LoggerInitialized()));
            Receive<Error>(error =>
            {
                var text = error.Message?.ToString() ?? string.Empty;
                if (text.IndexOf(FailureMarker, StringComparison.Ordinal) >= 0)
                    FuzzFailureSink.Record(Context.System.Name, error.ToString() ?? text);
            });
            // ignore everything else (Warning/Info/Debug) to keep the logger mailbox light
            ReceiveAny(_ => { });
        }
    }

    /// <summary>
    /// Randomized "startup fuzz" regression spec for the Cluster-extension startup race in
    /// <c>Cluster.cs</c>.
    ///
    /// <para>
    /// The bug: cluster actors spawned during the extension constructor — the read-view event-bus
    /// listener (internal dispatcher) and the SBR downing-provider actor (default dispatcher, a child of
    /// <c>ClusterCoreDaemon</c>) — reach <c>Cluster.ClusterCore</c> re-entrantly from their PreStart
    /// while the core is still being resolved, and <b>block a dispatcher thread</b> waiting for it (in
    /// the original code by issuing their own redundant blocking ask). On a single-thread default
    /// dispatcher this is a hard self-deadlock: the blocked thread is the only one the daemon that must
    /// make progress needs, so the constructor's <c>GetClusterCoreRef</c> ask cannot be answered and
    /// times out at <c>akka.actor.creation-timeout</c>. The catch block then calls <c>Shutdown()</c> —
    /// terminating the cluster — and a subsequent <c>JoinAsync</c> throws
    /// <see cref="ClusterJoinFailedException"/>("Cluster has already been terminated"). This is the exact
    /// signature that killed StressSpec on 2-core Windows CI agents.
    /// </para>
    ///
    /// <para>
    /// Design notes:
    /// <list type="bullet">
    /// <item><b>Single-thread default dispatcher</b> (<c>parallelism-min/max = 1</c>) is the shape under
    /// which a startup that blocks a dispatcher thread waiting for <c>ClusterCore</c> is a guaranteed
    /// deadlock. Iterations that draw a 2-thread default dispatcher generally start healthily and reach
    /// <c>Up</c> — the harness reports those passes, demonstrating it is not trivially always-failing.</item>
    /// <item><b>Generous, not tiny, creation-timeout.</b> A correct startup resolves the core in well
    /// under a second even on one thread, so any value in the 4-7s range leaves ample head-room; the
    /// deadlock never completes and trips regardless. The value only bounds how long each killed
    /// iteration blocks. This keeps the harness targeted at the deadlock rather than at generic CPU
    /// starvation (confirmed: it reproduces on an essentially idle box, loadavg ~2 on 8 cores).</item>
    /// <item><b>Concurrent creation + light hammering.</b> Systems are created concurrently and their
    /// <c>ClusterCore</c>-dependent surface (<c>Subscribe</c> / <c>SendCurrentClusterState</c>, plus
    /// actors that subscribe from PreStart) is driven from several tasks. This raises throughput/coverage
    /// and jitter; the deadlock itself needs no CPU burn to trip.</item>
    /// <item><b>A fix must make startup non-blocking to pass.</b> The harness fails any implementation
    /// that blocks a dispatcher thread on <c>ClusterCore</c> during construction — including the rejected
    /// single-flight/<c>TaskCompletionSource</c> variant, which still blocks (just on the shared task).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Kills are classified <c>immediate</c> (the cluster was already terminated when <c>Create</c>
    /// returned — the deadlock blocked the constructor's own ask) or <c>delayed</c> (<c>Create</c>
    /// returned healthy and the cluster was terminated within the watch window by a later blocked ask).
    /// Both share the same root cause and both fail the spec; the split is reported for diagnostics.
    /// </para>
    /// </summary>
    public sealed class ClusterStartupFuzzSpec : AkkaSpec
    {
        // ---- tunable knobs (consts so they are easy to sweep during tuning) ----
        private const int HardDeadlineSeconds = 52;     // absolute guard vs. the 60s xunit session timeout
        private const int PerIterationCapSeconds = 20;  // hard cap on any single system's lifecycle
        private const double WatchWindowExtraSeconds = 4.0; // watch = creation-timeout + this

        private static readonly Config BaseConfig = ConfigurationFactory.ParseString(@"
            akka.loglevel = WARNING
            akka.stdout-loglevel = WARNING");

        private readonly ITestOutputHelper _output;
        private readonly int _seed;

        public ClusterStartupFuzzSpec(ITestOutputHelper output)
            : base(BaseConfig, output)
        {
            _output = output;
            _seed = ResolveSeed();
        }

        private static int ResolveSeed()
        {
            var env = Environment.GetEnvironmentVariable("AKKA_FUZZ_SEED");
            if (!string.IsNullOrWhiteSpace(env) &&
                int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                return s;
            return new Random().Next(1, int.MaxValue);
        }

        // Optional env overrides used only while tuning the harness (unset in CI).
        private static int? EnvInt(string key)
            => int.TryParse(Environment.GetEnvironmentVariable(key), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var v) ? v : (int?)null;

        private static double? EnvDouble(string key)
            => double.TryParse(Environment.GetEnvironmentVariable(key), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var v) ? v : (double?)null;

        [Fact(DisplayName = "Should_not_terminate_a_healthy_cluster_during_concurrent_startup_hammering")]
        public Task Should_not_terminate_a_healthy_cluster_during_concurrent_startup_hammering()
            => RunFuzzAsync(new Profile(
                name: "moderate",
                aggressive: false,
                concurrency: 3,
                launchBudgetSeconds: 22,
                maxIterations: 60));

        [Fact(DisplayName = "Should_survive_aggressive_pinned_pool_startup_fuzz")]
        public Task Should_survive_aggressive_pinned_pool_startup_fuzz()
            => RunFuzzAsync(new Profile(
                name: "aggressive",
                aggressive: true,
                concurrency: 4,
                launchBudgetSeconds: 24,
                maxIterations: 72));

        private sealed record Profile(
            string name,
            bool aggressive,
            int concurrency,
            int launchBudgetSeconds,
            int maxIterations);

        private sealed record FuzzParams(
            int PoolDefault,
            int PoolInternal,
            double CreationTimeoutSeconds,
            int HammerThreads,
            int PreStartSubscribers,
            int SpinBudgetBeforeYield,
            bool MicroDelayBeforeCreate);

        private enum Outcome
        {
            Pass,
            ReachedUp,
            KillImmediate,   // already terminated when Create returned (deadlock blocked the ctor's own ask)
            KillDelayed,     // Create returned healthy, then terminated within the watch window by a later blocked ask
            CreateThrew,
            SoftNoConfirm    // never confirmed Up but was never killed either (machine load)
        }

        private readonly record struct IterationResult(Outcome Outcome, string Detail);

        private async Task RunFuzzAsync(Profile profile)
        {
            var concurrency = EnvInt("AKKA_FUZZ_CONCURRENCY") ?? profile.concurrency;
            var launchBudgetSeconds = EnvInt("AKKA_FUZZ_LAUNCH") ?? profile.launchBudgetSeconds;
            var maxIterations = EnvInt("AKKA_FUZZ_MAXITER") ?? profile.maxIterations;

            _output.WriteLine(
                $"[ClusterStartupFuzz:{profile.name}] seed={_seed} " +
                $"(override with AKKA_FUZZ_SEED) concurrency={concurrency} " +
                $"maxIterations={maxIterations} launchBudget={launchBudgetSeconds}s");

            var master = new Random(_seed);
            var failures = new ConcurrentQueue<string>();
            var killImmediate = 0;
            var killDelayed = 0;
            var reachedUp = 0;
            var passed = 0;
            var soft = 0;
            var completed = 0;

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(HardDeadlineSeconds));
            using var throttle = new SemaphoreSlim(concurrency);
            var inflight = new List<Task>();
            var sw = Stopwatch.StartNew();

            for (var iter = 0;
                 iter < maxIterations &&
                 sw.Elapsed < TimeSpan.FromSeconds(launchBudgetSeconds) &&
                 !deadline.IsCancellationRequested;
                 iter++)
            {
                try
                {
                    await throttle.WaitAsync(deadline.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var iterationSeed = master.Next();
                var thisIter = iter;
                inflight.Add(Task.Run(async () =>
                {
                    try
                    {
                        var result = await RunSingleSystemAsync(thisIter, iterationSeed, profile, deadline.Token)
                            .WaitAsync(TimeSpan.FromSeconds(PerIterationCapSeconds));

                        Interlocked.Increment(ref completed);
                        switch (result.Outcome)
                        {
                            case Outcome.KillImmediate:
                                Interlocked.Increment(ref killImmediate);
                                failures.Enqueue(result.Detail);
                                break;
                            case Outcome.KillDelayed:
                                Interlocked.Increment(ref killDelayed);
                                failures.Enqueue(result.Detail);
                                break;
                            case Outcome.CreateThrew:
                                failures.Enqueue(result.Detail);
                                break;
                            case Outcome.ReachedUp:
                                Interlocked.Increment(ref reachedUp);
                                Interlocked.Increment(ref passed);
                                break;
                            case Outcome.Pass:
                                Interlocked.Increment(ref passed);
                                break;
                            case Outcome.SoftNoConfirm:
                                Interlocked.Increment(ref soft);
                                break;
                        }
                    }
                    catch (TimeoutException)
                    {
                        Interlocked.Increment(ref soft);
                    }
                    // slopwatch-ignore: SW003 Hard-deadline cancellation while an iteration is tearing down; the outcome was already recorded before this fires, so there is nothing left to handle.
                    catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
                    {
                        // deadline hit while tearing down — ignore
                    }
                    finally
                    {
                        throttle.Release();
                    }
                }, CancellationToken.None));
            }

            await Task.WhenAll(inflight);

            _output.WriteLine(
                $"[ClusterStartupFuzz:{profile.name}] seed={_seed} completed={completed} in {sw.Elapsed.TotalSeconds:F1}s | " +
                $"kills(delayed={killDelayed}, immediate={killImmediate}) | pass={passed} (reachedUp={reachedUp}) | soft={soft}");

            foreach (var f in failures)
                _output.WriteLine($"[ClusterStartupFuzz:{profile.name}] KILL: {f}");

            if (!failures.IsEmpty)
            {
                Assert.Fail(
                    $"Cluster startup race reproduced: {failures.Count} healthy-cluster termination(s) " +
                    $"(delayed={killDelayed}, immediate={killImmediate}) across {completed} iterations. " +
                    $"Reproduce with AKKA_FUZZ_SEED={_seed}.");
            }

            Assert.True(completed > 0,
                $"Fuzz harness did not complete any iteration (seed={_seed}); machine likely overloaded.");
        }

        private FuzzParams BuildParams(Random rnd, bool aggressive)
        {
            // default pool == 1 is the deadlock-guaranteeing shape; keep it dominant.
            var poolDefault = EnvInt("AKKA_FUZZ_POOLD") ?? (rnd.Next(5) == 0 ? 2 : 1);   // ~80% -> 1
            var poolInternal = EnvInt("AKKA_FUZZ_POOLI") ?? (rnd.Next(2) == 0 ? 1 : 2);

            // Generous relative to a correct startup (a single ask round-trip is sub-second even on one
            // thread), yet short enough to keep throughput high. The failure is a hard deadlock that
            // never completes, so it trips regardless of the exact value; the length only bounds how
            // long each killed iteration blocks.
            var creationTimeout = EnvDouble("AKKA_FUZZ_TIMEOUT") ?? (aggressive
                ? 4.0 + rnd.NextDouble() * 2.0   // 4 - 6s
                : 5.0 + rnd.NextDouble() * 2.0); // 5 - 7s

            var hammerThreads = EnvInt("AKKA_FUZZ_HAMMER") ??
                (aggressive ? 3 + rnd.Next(4) : 2 + rnd.Next(3)); // 3-6 / 2-4
            var preStartSubs = EnvInt("AKKA_FUZZ_SUBS") ?? (2 + rnd.Next(aggressive ? 5 : 3)); // 2-6 / 2-4
            // Light Phase-1 jitter only: the deadlock does not need CPU starvation to trip, and heavy
            // spinning only slows throughput.
            var spinBudget = EnvInt("AKKA_FUZZ_SPIN") ?? (20 + rnd.Next(160));
            var microDelay = rnd.Next(2) == 0;

            return new FuzzParams(
                poolDefault, poolInternal, creationTimeout, hammerThreads,
                preStartSubs, spinBudget, microDelay);
        }

        private static Config BuildConfig(FuzzParams p)
        {
            string Fj(int parallelism) => $@"{{
                    executor = fork-join-executor
                    fork-join-executor {{
                        parallelism-min = {parallelism}
                        parallelism-max = {parallelism}
                        parallelism-factor = 1.0
                    }}
                }}";

            var timeout = p.CreationTimeoutSeconds.ToString("F2", CultureInfo.InvariantCulture);

            return ConfigurationFactory.ParseString($@"
                akka.loglevel = ERROR
                akka.stdout-loglevel = ERROR
                akka.loggers = [""Akka.Cluster.Tests.ClusterStartupFuzzLogger, Akka.Cluster.Tests""]

                akka.actor.provider = cluster
                akka.actor.creation-timeout = {timeout}s
                akka.actor.default-dispatcher {Fj(p.PoolDefault)}
                akka.actor.internal-dispatcher {Fj(p.PoolInternal)}

                akka.remote.dot-netty.tcp.hostname = ""127.0.0.1""
                akka.remote.dot-netty.tcp.port = 0

                akka.cluster.downing-provider-class = ""Akka.Cluster.SBR.SplitBrainResolverProvider, Akka.Cluster""
                akka.cluster.split-brain-resolver.active-strategy = keep-majority
                akka.cluster.periodic-tasks-initial-delay = 100ms
                akka.cluster.run-coordinated-shutdown-when-down = off
                akka.coordinated-shutdown.terminate-actor-system = off
                akka.coordinated-shutdown.run-by-actor-system-terminate = off
            ");
        }

        private async Task<IterationResult> RunSingleSystemAsync(
            int iter, int iterationSeed, Profile profile, CancellationToken ct)
        {
            var rnd = new Random(iterationSeed);
            var p = BuildParams(rnd, profile.aggressive);
            var name = $"fuzz-{_seed}-{profile.name}-{iter}";
            var config = BuildConfig(p);
            var tag =
                $"iter#{iter} (seed={_seed}, pools d={p.PoolDefault}/i={p.PoolInternal}, " +
                $"creation-timeout={p.CreationTimeoutSeconds:F2}s, hammer={p.HammerThreads})";

            FuzzFailureSink.Clear(name);

            ActorSystem? sys = null;
            var burnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var sysReady = new TaskCompletionSource<ActorSystem>(TaskCreationOptions.RunContinuationsAsynchronously);
            var burners = new List<Task>();

            try
            {
                // CPU burners contend for cores while ActorSystem.Create runs the Cluster construction,
                // widening the window in which the constructor thread is descheduled right after the
                // daemon replies but before it records the resolved core.
                for (var h = 0; h < p.HammerThreads; h++)
                    burners.Add(Task.Run(() => BurnThenHammerAsync(sysReady.Task, p, iterationSeed + h, burnCts.Token)));

                if (p.MicroDelayBeforeCreate)
                    await Task.Yield();

                try
                {
                    sys = await Task.Run(() => ActorSystem.Create(name, config), ct);
                    sysReady.TrySetResult(sys);
                }
                catch (Exception ex)
                {
                    sysReady.TrySetException(ex);
                    return new IterationResult(Outcome.CreateThrew,
                        $"{tag}: ActorSystem.Create threw {ex.GetType().Name}: {ex.Message}");
                }

                var cluster = Cluster.Get(sys);
                var terminatedAtCreate = cluster.IsTerminated;

                // Subscriber actors on the pinned dispatchers whose PreStart subscribes to cluster
                // events — mimicking SBR / the remote watcher — plus direct Subscribe / state pulls.
                var sink = sys.ActorOf(Props.Create(() => new SinkActor()), "fuzz-sink");
                for (var s = 0; s < p.PreStartSubscribers; s++)
                {
                    var dispatcher = (s % 2 == 0)
                        ? "akka.actor.internal-dispatcher"
                        : "akka.actor.default-dispatcher";
                    sys.ActorOf(
                        Props.Create(() => new SubscriberActor()).WithDispatcher(dispatcher),
                        $"fuzz-sub-{s}");
                }

                for (var k = 0; k < 4; k++)
                {
                    try
                    {
                        cluster.Subscribe(sink, typeof(ClusterEvent.IClusterDomainEvent));
                        cluster.SendCurrentClusterState(sink);
                    }
                    // slopwatch-ignore: SW003 The cluster may already be terminated by the startup race under test; this retry loop is best-effort probing and the actual outcome is captured via FuzzFailureSink/IsTerminated below.
                    catch
                    {
                        /* system may be shutting down — expected under the race */
                    }
                }

                // Brief hammer window, then quiesce burners so the (single-thread) system has room to
                // either reach Up (healthy) or let the redundant-ask deadlock time out (raced).
                await Task.Delay(80 + rnd.Next(160), ct);
                burnCts.Cancel();

                var watch = TimeSpan.FromSeconds(p.CreationTimeoutSeconds + WatchWindowExtraSeconds);

                using var joinCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                joinCts.CancelAfter(watch);
                Task joinTask;
                try
                {
                    joinTask = cluster.JoinAsync(cluster.SelfAddress, joinCts.Token);
                }
                catch (ClusterJoinFailedException)
                {
                    // Synchronous throw => already terminated when we tried to join.
                    joinTask = Task.CompletedTask; // don't observe it again
                }

                var sw = Stopwatch.StartNew();
                Outcome outcome = Outcome.SoftNoConfirm;
                string? marker = null;

                while (sw.Elapsed < watch + TimeSpan.FromSeconds(1))
                {
                    if (cluster.IsTerminated)
                    {
                        outcome = terminatedAtCreate ? Outcome.KillImmediate : Outcome.KillDelayed;
                        break;
                    }

                    if (FuzzFailureSink.TryGet(name, out var m))
                    {
                        marker = m;
                        outcome = terminatedAtCreate ? Outcome.KillImmediate : Outcome.KillDelayed;
                        break;
                    }

                    if (joinTask.IsCompletedSuccessfully)
                    {
                        outcome = Outcome.ReachedUp;
                        break;
                    }

                    try
                    {
                        await Task.Delay(75, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                // final re-check (covers the marker/termination landing right at the window edge)
                if (outcome is Outcome.SoftNoConfirm)
                {
                    if (cluster.IsTerminated || FuzzFailureSink.TryGet(name, out marker))
                        outcome = terminatedAtCreate ? Outcome.KillImmediate : Outcome.KillDelayed;
                    else if (joinTask.IsCompletedSuccessfully)
                        outcome = Outcome.ReachedUp;
                    else
                        outcome = Outcome.Pass; // survived the whole window without termination
                }

                FuzzFailureSink.TryGet(name, out var finalMarker);
                var markerText = marker ?? (string.IsNullOrEmpty(finalMarker) ? null : finalMarker);

                if (outcome is Outcome.KillImmediate or Outcome.KillDelayed)
                {
                    var kind = outcome == Outcome.KillDelayed
                        ? "DELAYED (healthy Create, then redundant-ask deadlock Shutdown)"
                        : "IMMEDIATE (terminated during Create)";
                    var detail =
                        $"{tag}: {kind}; IsTerminated={cluster.IsTerminated}; " +
                        $"marker={(markerText is not null)}" +
                        (markerText is not null ? $" [{Truncate(markerText, 220)}]" : string.Empty);
                    return new IterationResult(outcome, detail);
                }

                return new IterationResult(outcome, tag);
            }
            finally
            {
                if (!burnCts.IsCancellationRequested)
                    burnCts.Cancel();

                try
                {
                    await Task.WhenAll(burners).WaitAsync(TimeSpan.FromSeconds(5));
                }
                // slopwatch-ignore: SW003 Draining the CPU-burner tasks is best-effort cleanup; a burner exception or the 5s WaitAsync timeout must never fail the fuzz iteration itself.
                catch
                {
                    /* burners are best-effort */
                }

                if (sys is not null)
                {
                    try
                    {
                        await sys.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    // slopwatch-ignore: SW003 Teardown is intentionally bounded and best-effort; a system wedged by the deadlock under test is abandoned here and GC'd with the test process rather than failing the spec.
                    catch
                    {
                        /* bounded teardown; a wedged system is GC'd with the test process */
                    }
                }

                burnCts.Dispose();
                FuzzFailureSink.Clear(name);
            }
        }

        private static async Task BurnThenHammerAsync(
            Task<ActorSystem> sysReady, FuzzParams p, int seed, CancellationToken ct)
        {
            var rnd = new Random(seed);

            // Phase 1: contend for CPU while Create is running (until the system is ready or told to stop).
            // SpinBudget == 0 => cooperative jitter only (yields/tiny delays), which provides scheduling
            // pressure without hard-starving the single-thread pools.
            while (!sysReady.IsCompleted && !ct.IsCancellationRequested)
            {
                if (p.SpinBudgetBeforeYield <= 0)
                {
                    await Task.Yield();
                    continue;
                }

                var spin = new SpinWait();
                for (var i = 0; i < p.SpinBudgetBeforeYield && !sysReady.IsCompleted; i++)
                    spin.SpinOnce();
                await Task.Yield();
            }

            ActorSystem sys;
            try
            {
                sys = await sysReady;
            }
            catch
            {
                return;
            }

            IActorRef? sink = null;
            try
            {
                sink = sys.ActorOf(Props.Create(() => new SinkActor()));
            }
            // slopwatch-ignore: SW003 The target system may already be terminating from the deadlock under test; failing to create the sink actor here is expected and the hammer loop below falls back to DeadLetters.
            catch
            {
                // system already terminating
            }

            // Phase 2: keep touching the ClusterCore-dependent surface under load until quiesced.
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var cluster = Cluster.Get(sys);
                    var target = sink ?? sys.DeadLetters;
                    cluster.Subscribe(target, typeof(ClusterEvent.IClusterDomainEvent));
                    cluster.SendCurrentClusterState(target);
                }
                // slopwatch-ignore: SW003 This loop hammers ClusterCore concurrently with the startup race under test, so the target system may be mid-shutdown; failures here are expected and the loop simply retries.
                catch
                {
                    // expected while the system is shutting down
                }

                if (rnd.Next(3) == 0)
                {
                    var spin = new SpinWait();
                    for (var i = 0; i < 64; i++)
                        spin.SpinOnce();
                }

                await Task.Yield();
            }
        }

        private static string Truncate(string s, int max)
            => s.Length <= max ? s : s.Substring(0, max) + "...";

        /// <summary>Subscribes to cluster events from PreStart, like SBR / the remote watcher do.</summary>
        private sealed class SubscriberActor : ReceiveActor
        {
            public SubscriberActor() => ReceiveAny(_ => { });

            protected override void PreStart()
                => Cluster.Get(Context.System).Subscribe(Self, typeof(ClusterEvent.IClusterDomainEvent));
        }

        /// <summary>Inert message sink for Subscribe / SendCurrentClusterState targets.</summary>
        private sealed class SinkActor : ReceiveActor
        {
            public SinkActor() => ReceiveAny(_ => { });
        }
    }
}
