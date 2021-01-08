//-----------------------------------------------------------------------
// <copyright file="CoordinatedShutdown.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal;
using static Akka.Pattern.FutureTimeoutSupport;
using static Akka.Util.Internal.TaskEx;
using Config = Akka.Configuration.Config;

namespace Akka.Actor
{
    /// <summary>
    /// Used to register the <see cref="CoordinatedShutdown"/> extension with a given <see cref="ActorSystem"/>.
    /// </summary>
    public sealed class CoordinatedShutdownExtension : ExtensionIdProvider<CoordinatedShutdown>
    {
        /// <summary>
        /// Creates a new instance of the <see cref="CoordinatedShutdown"/> extension.
        /// </summary>
        /// <param name="system">The extended actor system.</param>
        /// <returns>A coordinated shutdown plugin.</returns>
        public override CoordinatedShutdown CreateExtension(ExtendedActorSystem system)
        {
            var conf = system.Settings.Config.GetConfig("akka.coordinated-shutdown");
            if (conf.IsNullOrEmpty())
                throw new ConfigurationException("akka.coordinated-shutdown config cannot be empty");

            var phases = CoordinatedShutdown.PhasesFromConfig(conf);
            var coord = new CoordinatedShutdown(system, phases);
            CoordinatedShutdown.InitPhaseActorSystemTerminate(system, conf, coord);
            CoordinatedShutdown.InitClrHook(system, conf, coord);
            return coord;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Phase
    {
        /// <summary>
        /// Creates a new <see cref="Phase"/>
        /// </summary>
        /// <param name="dependsOn">The list of other phases this phase depends upon.</param>
        /// <param name="timeout">A timeout value for any tasks running during this phase.</param>
        /// <param name="recover">When set to <c>true</c>, this phase can recover from a faulted state during shutdown.</param>
        public Phase(ImmutableHashSet<string> dependsOn, TimeSpan timeout, bool recover)
        {
            DependsOn = dependsOn ?? ImmutableHashSet<string>.Empty;
            Timeout = timeout;
            Recover = recover;
        }

        /// <summary>
        /// The names of other <see cref="Phase"/>s this phase depends upon.
        /// </summary>
        public ImmutableHashSet<string> DependsOn { get; }

        /// <summary>
        /// The amount of time this phase is allowed to run.
        /// </summary>
        public TimeSpan Timeout { get; }

        /// <summary>
        /// If <c>true</c>, this phase has the ability to recover during a faulted state.
        /// </summary>
        public bool Recover { get; }

        private bool Equals(Phase other)
        {
            return DependsOn.SetEquals(other.DependsOn)
                && Timeout.Equals(other.Timeout)
                && Recover == other.Recover;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Phase && Equals((Phase)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = DependsOn?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ Timeout.GetHashCode();
                hashCode = (hashCode * 397) ^ Recover.GetHashCode();
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"DependsOn=[{string.Join(",", DependsOn)}], Timeout={Timeout}, Recover={Recover}";
        }
    }

    /// <summary>
    /// An <see cref="ActorSystem"/> extension used to help coordinate and sequence shutdown activities
    /// during graceful termination of actor systems, plugins, and so forth.
    /// </summary>
    public sealed class CoordinatedShutdown : IExtension
    {
        /// <summary>
        /// Initializes a new <see cref="CoordinatedShutdown"/> instance.
        /// </summary>
        /// <param name="system">Access to the <see cref="ExtendedActorSystem"/>.</param>
        /// <param name="phases">The list of <see cref="Phase"/>s provided by the HOCON configuration.</param>
        internal CoordinatedShutdown(ExtendedActorSystem system, Dictionary<string, Phase> phases)
        {
            System = system;
            Phases = phases;
            Log = Logging.GetLogger(System, GetType());
            _knownPhases = new HashSet<string>(Phases.Keys.Concat(Phases.Values.SelectMany(x => x.DependsOn)));
            OrderedPhases = TopologicalSort(Phases);
        }

        /// <summary>
        /// Retrieves the <see cref="CoordinatedShutdown"/> extension for the current <see cref="ActorSystem"/>
        /// </summary>
        /// <param name="sys">The current actor system.</param>
        /// <returns>A <see cref="CoordinatedShutdown"/> instance.</returns>
        public static CoordinatedShutdown Get(ActorSystem sys)
        {
            return sys.WithExtension<CoordinatedShutdown, CoordinatedShutdownExtension>();
        }

        public const string PhaseBeforeServiceUnbind = "before-service-unbind";
        public const string PhaseServiceUnbind = "service-unbind";
        public const string PhaseServiceRequestsDone = "service-requests-done";
        public const string PhaseServiceStop = "service-stop";
        public const string PhaseBeforeClusterShutdown = "before-cluster-shutdown";
        public const string PhaseClusterShardingShutdownRegion = "cluster-sharding-shutdown-region";
        public const string PhaseClusterLeave = "cluster-leave";
        public const string PhaseClusterExiting = "cluster-exiting";
        public const string PhaseClusterExitingDone = "cluster-exiting-done";
        public const string PhaseClusterShutdown = "cluster-shutdown";
        public const string PhaseBeforeActorSystemTerminate = "before-actor-system-terminate";
        public const string PhaseActorSystemTerminate = "actor-system-terminate";



        /// <summary>
        /// Reason for the shutdown, which can be used by tasks in case they need to do
        /// different things depending on what caused the shutdown. There are some
        /// predefined reasons, but external libraries applications may also define
        /// other reasons.
        /// </summary>
        public class Reason
        {
            protected Reason()
            {

            }
        }

        /// <summary>
        /// The reason for the shutdown was unknown. Needed for backwards compatibility.
        /// </summary>
        public class UnknownReason : Reason
        {
            public static readonly Reason Instance = new UnknownReason();

            private UnknownReason()
            {

            }
        }

        /// <summary>
        /// The shutdown was initiated by an ActorSystem termination hook
        /// </summary>
        public class ActorSystemTerminateReason : Reason
        {
            public static readonly Reason Instance = new ActorSystemTerminateReason();

            private ActorSystemTerminateReason()
            {

            }
        }

        /// <summary>
        /// The shutdown was initiated by a CLR shutdown hook
        /// </summary>
        public class ClrExitReason : Reason
        {
            public static readonly Reason Instance = new ClrExitReason();

            private ClrExitReason()
            {

            }
        }


        /// <summary>
        /// The shutdown was initiated by Cluster downing.
        /// </summary>
        public class ClusterDowningReason : Reason
        {
            public static readonly Reason Instance = new ClusterDowningReason();

            private ClusterDowningReason()
            {

            }
        }


        /// <summary>
        /// The shutdown was initiated by Cluster leaving.
        /// </summary>
        public class ClusterLeavingReason : Reason
        {
            public static readonly Reason Instance = new ClusterLeavingReason();

            private ClusterLeavingReason()
            {

            }
        }

        /// <summary>
        /// The shutdown was initiated by a failure to join a seed node.
        /// </summary>
        public class ClusterJoinUnsuccessfulReason : Reason
        {
            public static readonly Reason Instance = new ClusterJoinUnsuccessfulReason();
            private ClusterJoinUnsuccessfulReason() { }
        }

        /// <summary>
        /// The <see cref="ActorSystem"/>
        /// </summary>
        public ExtendedActorSystem System { get; }

        /// <summary>
        /// The set of named <see cref="Phase"/>s that will be executed during coordinated shutdown.
        /// </summary>
        internal Dictionary<string, Phase> Phases { get; }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        internal ILoggingAdapter Log { get; }

        private readonly HashSet<string> _knownPhases;

        /// <summary>
        /// INTERNAL API
        /// </summary>
        internal readonly List<string> OrderedPhases;

        private readonly ConcurrentBag<Func<Task<Done>>> _clrShutdownTasks = new ConcurrentBag<Func<Task<Done>>>();
        private readonly ConcurrentDictionary<string, ImmutableList<(string, Func<Task<Done>>)>> _tasks = new ConcurrentDictionary<string, ImmutableList<(string, Func<Task<Done>>)>>();
        private readonly AtomicReference<Reason> _runStarted = new AtomicReference<Reason>(null);
        private readonly AtomicBoolean _clrHooksStarted = new AtomicBoolean(false);
        private readonly TaskCompletionSource<Done> _runPromise = new TaskCompletionSource<Done>();
        private readonly TaskCompletionSource<Done> _hooksRunPromise = new TaskCompletionSource<Done>();

        private volatile bool _runningClrHook = false;

        /// <summary>
        /// INTERNAL API
        ///
        /// Signals when CLR shutdown hooks have been completed
        /// </summary>
        internal Task<Done> ClrShutdownTask => _hooksRunPromise.Task;

        /// <summary>
        /// Add a task to a phase. It doesn't remove previously added tasks.
        ///
        /// Tasks added to the same phase are executed in parallel without any
        /// ordering assumptions. Next phase will not start until all tasks of
        /// previous phase have completed.
        /// </summary>
        /// <param name="phase">The phase to add this task to.</param>
        /// <param name="taskName">The name of the task to add to this phase.</param>
        /// <param name="task">The delegate that produces a <see cref="Task"/> that will be executed.</param>
        /// <remarks>
        /// Tasks should typically be registered as early as possible after system
        /// startup. When running the <see cref="CoordinatedShutdown"/> tasks that have been
        /// registered will be performed but tasks that are added too late will not be run.
        ///
        ///
        /// It is possible to add a task to a later phase from within a task in an earlier phase
        /// and it will be performed.
        /// </remarks>
        public void AddTask(string phase, string taskName, Func<Task<Done>> task)
        {
            if (!_knownPhases.Contains(phase))
                throw new ConfigurationException($"Unknown phase [{phase}], known phases [{string.Join(",", _knownPhases)}]. " +
                    "All phases (along with their optional dependencies) must be defined in configuration.");

            if (!_tasks.TryGetValue(phase, out var current))
            {
                if (!_tasks.TryAdd(phase, ImmutableList<(string, Func<Task<Done>>)>.Empty.Add((taskName, task))))
                    AddTask(phase, taskName, task); // CAS failed, retry
            }
            else
            {
                if (!_tasks.TryUpdate(phase, current.Add((taskName, task)), current))
                    AddTask(phase, taskName, task); // CAS failed, retry
            }
        }

        /// <summary>
        /// Add a shutdown hook that will execute when the CLR process begins
        /// its shutdown sequence, invoked via <see cref="AppDomain.ProcessExit"/>.
        ///
        /// Added hooks may run in any order concurrently, but they are run before
        /// the Akka.NET internal shutdown hooks execute.
        /// </summary>
        /// <param name="hook">A task that will be executed during shutdown.</param>
        internal void AddClrShutdownHook(Func<Task<Done>> hook)
        {
            if (!_clrHooksStarted)
            {
                _clrShutdownTasks.Add(hook);
            }
        }


        /// <summary>
        /// INTERNAL API
        ///
        /// Should only be called directly by the <see cref="AppDomain.ProcessExit"/> event
        /// in production.
        ///
        /// Safe to call multiple times, but hooks will only be run once.
        /// </summary>
        /// <returns>Returns a <see cref="Task"/> that will be completed once the process exits.</returns>
        private Task<Done> RunClrHooks()
        {
            if (_clrHooksStarted.CompareAndSet(false, true))
            {
                Task.WhenAll(_clrShutdownTasks.Select(hook =>
                {
                    try
                    {
                        var t = hook();
                        return t;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error occurred while executing CLR shutdown hook");
                        return TaskEx.FromException<Done>(ex);
                    }
                })).ContinueWith(tr =>
                {
                    if (tr.IsFaulted || tr.IsCanceled)
                        _hooksRunPromise.SetException(tr.Exception.Flatten());
                    else
                    {
                        _hooksRunPromise.SetResult(Done.Instance);
                    }
                });
            }

            return ClrShutdownTask;
        }

        /// <summary>
        /// The <see cref="Reason"/> for the shutdown as passed to the <see cref="Run(Reason, string)"/> method. <see langword="null"/> if the shutdown
        /// has not been started.
        /// </summary>
        public Reason ShutdownReason => _runStarted.Value;
        
        /// <summary>
        /// Run tasks of all phases including and after the given phase.
        /// </summary>
        /// <param name="reason">Reason of the shutdown</param>
        /// <param name="fromPhase">Optional. The phase to start the run from.</param>
        /// <returns>A task that is completed when all such tasks have been completed, or
        /// there is failure when <see cref="Phase.Recover"/> is disabled.</returns>
        /// <remarks>
        /// It is safe to call this method multiple times. It will only run the shutdown sequence once.
        /// </remarks>
        public Task<Done> Run(Reason reason, string fromPhase = null)
        {
            if (_runStarted.CompareAndSet(null, reason))
            {
                var debugEnabled = Log.IsDebugEnabled;

                Task<Done> Loop(List<string> remainingPhases)
                {
                    var phase = remainingPhases.FirstOrDefault();
                    if (phase == null)
                        return TaskEx.Completed;
                    var remaining = remainingPhases.Skip(1).ToList();
                    Task<Done> phaseResult = null;
                    if (!_tasks.TryGetValue(phase, out var phaseTasks))
                    {
                        if (debugEnabled)
                            Log.Debug("Performing phase [{0}] with [0] tasks.", phase);
                        phaseResult = TaskEx.Completed;
                    }
                    else
                    {
                        if (debugEnabled)
                            Log.Debug("Performing phase [{0}] with [{1}] tasks: [{2}]", phase, phaseTasks.Count, string.Join(",", phaseTasks.Select(x => x.Item1)));

                        // note that tasks within same phase are performed in parallel
                        var recoverEnabled = Phases[phase].Recover;
                        var result = Task.WhenAll<Done>(phaseTasks.Select(x =>
                            {
                                var taskName = x.Item1;
                                var task = x.Item2;
                                try
                                {
                                    // need to begin execution of task
                                    var r = task();

                                    if (recoverEnabled)
                                    {
                                        return r.ContinueWith(tr =>
                                        {
                                            if (tr.IsCanceled || tr.IsFaulted)
                                                Log.Warning("Task [{0}] failed in phase [{1}]: {2}", taskName, phase, tr.Exception?.Flatten().Message);
                                            return Done.Instance;
                                        });
                                    }

                                    return r;
                                }
                                catch (Exception ex)
                                {
                                    // in case task.Start() throws
                                    if (recoverEnabled)
                                    {
                                        Log.Warning("Task [{0}] failed in phase [{1}]: {2}", taskName, phase, ex.Message);
                                        return TaskEx.Completed;
                                    }

                                    return TaskEx.FromException<Done>(ex);
                                }
                            }))
                            .ContinueWith(tr =>
                            {
                                // forces downstream error propagation if recover is disabled
                                var force = tr.Result;
                                return Done.Instance;
                            });
                        var timeout = Phases[phase].Timeout;
                        var deadLine = MonotonicClock.Elapsed + timeout;
                        Task<Done> timeoutFunction = null;
                        try
                        {
                            timeoutFunction = After(timeout, System.Scheduler, () =>
                            {
                                if (phase == CoordinatedShutdown.PhaseActorSystemTerminate && MonotonicClock.ElapsedHighRes < deadLine)
                                    return result; // too early, i.e. triggered by system termination

                                if (result.IsCompleted)
                                    return TaskEx.Completed;

                                if (recoverEnabled)
                                {
                                    Log.Warning("Coordinated shutdown phase [{0}] timed out after {1}", phase, timeout);
                                    return TaskEx.Completed;
                                }

                                return TaskEx.FromException<Done>(new TimeoutException($"Coordinated shutdown phase[{phase}] timed out after {timeout}"));
                            });
                        }
                        catch (SchedulerException)
                        {
                            // The call to `after` threw SchedulerException, triggered by system termination
                            timeoutFunction = result;
                        }
                        catch (InvalidOperationException)
                        {
                            // The call to `after` threw SchedulerException, triggered by Scheduler being in unset state
                            timeoutFunction = result;
                        }

                        phaseResult = Task.WhenAny<Done>(result, timeoutFunction).Unwrap();
                    }

                    if (!remaining.Any())
                        return phaseResult;
                    return phaseResult.ContinueWith(tr =>
                        {
                            // force any exceptions to be rethrown so next phase stops
                            // and so failure gets propagated back to caller
                            var r = tr.Result;
                            return Loop(remaining);
                        })
                        .Unwrap<Done>();
                }

                var runningPhases = (fromPhase == null
                    ? OrderedPhases // all
                    : OrderedPhases.From(fromPhase)).ToList();

                var done = Loop(runningPhases);
                done.ContinueWith(tr =>
                {
                    if (!tr.IsFaulted && !tr.IsCanceled)
                        _runPromise.SetResult(tr.Result);
                    else
                    {
                        // ReSharper disable once PossibleNullReferenceException
                        _runPromise.SetException(tr.Exception.Flatten());
                    }
                });
            }
            return _runPromise.Task;
        }

        /// <summary>
        /// The configured timeout for a given <see cref="Phase"/>.
        /// </summary>
        /// <param name="phase">The name of the phase.</param>
        /// <exception cref="ArgumentException">Thrown if <paramref name="phase"/> doesn't exist in the set of registered phases.</exception>
        /// <returns>Returns the timeout if ti exists.</returns>
        public TimeSpan Timeout(string phase)
        {
            if (Phases.TryGetValue(phase, out var p))
                return p.Timeout;
            throw new ArgumentException($"Unknown phase [{phase}]. All phases must be defined in configuration.");
        }

        /// <summary>
        /// The sum of timeouts of all phases that have some task.
        /// </summary>
        public TimeSpan TotalTimeout
        {
            get { return _tasks.Keys.Aggregate(TimeSpan.Zero, (span, s) => span.Add(Timeout(s))); }
        }

        // TODO: do we need to check for null or empty config here?
        /// <summary>
        /// INTERNAL API
        /// </summary>
        /// <param name="config">The HOCON configuration for the <see cref="CoordinatedShutdown"/></param>
        /// <returns>A map of all of the phases of the shutdown.</returns>
        internal static Dictionary<string, Phase> PhasesFromConfig(Config config)
        {
            if (config.IsNullOrEmpty())
                throw new ConfigurationException("Invalid phase configuration.");

            var defaultPhaseTimeout = config.GetString("default-phase-timeout", null);
            var phasesConf = config.GetConfig("phases");
            var defaultPhaseConfig = ConfigurationFactory.ParseString($"timeout = {defaultPhaseTimeout}" + @"
                recover = true
                depends-on = []
            ");

            return phasesConf.Root.GetObject().Unwrapped.ToDictionary(x => x.Key, v =>
             {
                 var c = phasesConf.GetConfig(v.Key).WithFallback(defaultPhaseConfig);
                 var dependsOn = c.GetStringList("depends-on").ToImmutableHashSet();
                 var timeout = c.GetTimeSpan("timeout", null, allowInfinite: false);
                 var recover = c.GetBoolean("recover", false);
                 return new Phase(dependsOn, timeout, recover);
             });
        }

        /// <summary>
        /// INTERNAL API: https://en.wikipedia.org/wiki/Topological_sorting
        /// </summary>
        /// <param name="phases">The set of phases to sort.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when a cycle is detected in the phase graph.
        /// The graph must be a directed acyclic graph (DAG).
        /// </exception>
        /// <returns>A topologically sorted list of phases.</returns>
        internal static List<string> TopologicalSort(Dictionary<string, Phase> phases)
        {
            var result = new List<string>();
            // in case dependent phase is not defined as key
            var unmarked = new HashSet<string>(phases.Keys.Concat(phases.Values.SelectMany(x => x.DependsOn)));
            var tempMark = new HashSet<string>(); // for detecting cycles

            void DepthFirstSearch(string u)
            {
                if (tempMark.Contains(u))
                    throw new ArgumentException("Cycle detected in graph of phases. It must be a DAG. " + $"phase [{u}] depepends transitively on itself. All dependencies: {phases}");
                if (unmarked.Contains(u))
                {
                    tempMark.Add(u);
                    if (phases.TryGetValue(u, out var p) && p.DependsOn.Any())
                        p.DependsOn.ForEach(DepthFirstSearch);
                    unmarked.Remove(u); //permanent mark
                    tempMark.Remove(u);
                    result = new[] { u }.Concat(result).ToList();
                }
            }

            while (unmarked.Any())
            {
                DepthFirstSearch(unmarked.Head());
            }

            result.Reverse();
            return result;
        }

        // TODO: do we need to check for null or empty config here?
        /// <summary>
        /// INTERNAL API
        ///
        /// Primes the <see cref="CoordinatedShutdown"/> with the default phase for
        /// <see cref="ActorSystem.Terminate"/>
        /// </summary>
        /// <param name="system">The actor system for this extension.</param>
        /// <param name="conf">The HOCON configuration.</param>
        /// <param name="coord">The <see cref="CoordinatedShutdown"/> plugin instance.</param>
        internal static void InitPhaseActorSystemTerminate(ActorSystem system, Config conf, CoordinatedShutdown coord)
        {
            var terminateActorSystem = system.Settings.CoordinatedShutdownTerminateActorSystem;
            var exitClr = conf.GetBoolean("exit-clr", false);
            if (terminateActorSystem || exitClr)
            {
                coord.AddTask(PhaseActorSystemTerminate, "terminate-system", () =>
                {
                    if (exitClr && terminateActorSystem)
                    {
                        // In case ActorSystem shutdown takes longer than the phase timeout,
                        // exit the JVM forcefully anyway.

                        // We must spawn a separate Task to not block current thread,
                        // since that would have blocked the shutdown of the ActorSystem.
                        var timeout = coord.Timeout(PhaseActorSystemTerminate);
                        Task.Run(() =>
                        {
                            if (!system.WhenTerminated.Wait(timeout) && !coord._runningClrHook)
                            {
                                Environment.Exit(0);
                            }
                        });
                    }

                    if (terminateActorSystem)
                    {
                        system.FinalTerminate();
                        return system.Terminate().ContinueWith(tr =>
                        {
                            if (exitClr && !coord._runningClrHook)
                            {
                                Environment.Exit(0);
                            }
                            return Done.Instance;
                        });
                    }
                    else if (exitClr)
                    {
                        Environment.Exit(0);
                        return TaskEx.Completed;
                    }
                    else
                    {
                        return TaskEx.Completed;
                    }
                });
            }
        }

        // TODO: do we need to check for null or empty config here?
        /// <summary>
        /// Initializes the CLR hook
        /// </summary>
        /// <param name="system">The actor system for this extension.</param>
        /// <param name="conf">The HOCON configuration.</param>
        /// <param name="coord">The <see cref="CoordinatedShutdown"/> plugin instance.</param>
        internal static void InitClrHook(ActorSystem system, Config conf, CoordinatedShutdown coord)
        {
            var runByClrShutdownHook = conf.GetBoolean("run-by-clr-shutdown-hook", false);
            if (runByClrShutdownHook)
            {
                var exitTask = TerminateOnClrExit(coord);
                // run all hooks during termination sequence
                AppDomain.CurrentDomain.ProcessExit += exitTask;
                system.WhenTerminated.ContinueWith(tr => { AppDomain.CurrentDomain.ProcessExit -= exitTask; });

                coord.AddClrShutdownHook(() =>
                {
                    coord._runningClrHook = true;
                    return Task.Run(() =>
                    {
                        if (!system.WhenTerminated.IsCompleted)
                        {
                            coord.Log.Info("Starting coordinated shutdown from CLR termination hook.");
                            try
                            {
                                coord.Run(ClrExitReason.Instance).Wait(coord.TotalTimeout);
                            }
                            catch (Exception ex)
                            {
                                coord.Log.Warning("CoordinatedShutdown from CLR shutdown failed: {0}", ex.Message);
                            }
                        }
                        return Done.Instance;
                    });
                });
            }
        }

        private static EventHandler TerminateOnClrExit(CoordinatedShutdown coord)
        {
            return (sender, args) =>
            {
                // have to block, because if this method exits the process exits.
                coord.RunClrHooks().Wait(coord.TotalTimeout);
            };
        }
    }
}
