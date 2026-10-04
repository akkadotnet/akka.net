//-----------------------------------------------------------------------
// <copyright file="ActorMaterializerImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Annotations;
using Akka.Dispatch;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Implementation.Fusing;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// ExtendedActorMaterializer used by subtypes which materializer using GraphInterpreterShell
    /// </summary>
    public abstract class ExtendedActorMaterializer : ActorMaterializer
    {
        /// <summary>
        /// INTERNAL API
        /// </summary>
        /// <typeparam name="TMat">The materialized value type of the graph.</typeparam>
        /// <param name="runnable">The closed graph to materialize.</param>
        /// <param name="subFlowFuser">Creates an actor for an eligible fused subflow shell.</param>
        /// <returns>The graph's materialized value.</returns>
        [InternalApi]
        public abstract TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Func<GraphInterpreterShell, IActorRef> subFlowFuser);

        /// <summary>
        /// INTERNAL API
        /// </summary>
        /// <typeparam name="TMat">The materialized value type of the graph.</typeparam>
        /// <param name="runnable">The closed graph to materialize.</param>
        /// <param name="subFlowFuser">Creates an actor for an eligible fused subflow shell.</param>
        /// <param name="initialAttributes">The attributes inherited by the graph.</param>
        /// <returns>The graph's materialized value.</returns>
        [InternalApi]
        public abstract TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Func<GraphInterpreterShell, IActorRef> subFlowFuser, Attributes initialAttributes);

        /// <summary>
        /// INTERNAL API
        /// </summary>
        /// <param name="context">The materialization context providing effective attributes and an actor name.</param>
        /// <param name="props">The actor properties to instantiate.</param>
        /// <returns>The created actor reference.</returns>
        [InternalApi]
        public override IActorRef ActorOf(MaterializationContext context, Props props)
        {
            var dispatcher = props.Deploy.Dispatcher == Deploy.NoDispatcherGiven
                ? EffectiveSettings(context.EffectiveAttributes).Dispatcher
                : props.Dispatcher;

            return ActorOf(props, context.StageName, dispatcher);
        }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        /// <param name="props">The actor properties to instantiate.</param>
        /// <param name="name">The child actor name.</param>
        /// <param name="dispatcher">The dispatcher identifier applied to the actor.</param>
        /// <exception cref="IllegalStateException">The stream supervisor is not a local actor reference.</exception>
        /// <returns>The created child actor reference.</returns>
        [InternalApi]
        protected IActorRef ActorOf(Props props, string name, string dispatcher)
        {
            switch (Supervisor)
            {
                case LocalActorRef localActorRef:
                    return ((ActorCell) localActorRef.Underlying).AttachChild(props.WithDispatcher(dispatcher),
                        isSystemService: false, name: name);
                case RepointableActorRef { IsStarted: true } repointableActorRef:
                    return ((ActorCell)repointableActorRef.Underlying).AttachChild(props.WithDispatcher(dispatcher), isSystemService: false, name: name);
                case RepointableActorRef repointableActorRef:
                {
                    var timeout = repointableActorRef.Underlying.System.Settings.CreationTimeout;
                    var f = repointableActorRef.Ask<IActorRef>(new StreamSupervisor.Materialize(props.WithDispatcher(dispatcher), name), timeout);
                    return f.Result;
                }
                default:
                    throw new IllegalStateException($"Stream supervisor must be a local actor, was [{Supervisor.GetType()}]");
            }
        }
    }

    /// <summary>
    /// Default implementation of <see cref="ActorMaterializer"/>.
    /// </summary>
    public sealed class ActorMaterializerImpl : ExtendedActorMaterializer
    {
        #region Materializer session implementation

        private sealed class ActorMaterializerSession : MaterializerSession
        {
            private readonly ActorMaterializerImpl _materializer;
            private readonly Func<GraphInterpreterShell, IActorRef> _subflowFuser;
            private readonly string _flowName;
            private int _nextId;

            public ActorMaterializerSession(ActorMaterializerImpl materializer, IModule topLevel, Attributes initialAttributes, Func<GraphInterpreterShell, IActorRef> subflowFuser)
                : base(topLevel, initialAttributes)
            {
                _materializer = materializer;
                _subflowFuser = subflowFuser;
                _flowName = _materializer.CreateFlowName();
            }

            protected override object MaterializeAtomic(AtomicModule atomic, Attributes effectiveAttributes,
                IDictionary<IModule, object> materializedValues)
            {
                if(IsDebug)
                    Console.WriteLine($"materializing {atomic}");

                switch (atomic)
                {
                    case ISinkModule sink:
                    {
                        var subscriber = sink.Create(CreateMaterializationContext(effectiveAttributes), out var materialized);
                        AssignPort(sink.Shape.Inlets.First(), subscriber);
                        materializedValues.Add(atomic, materialized);
                        break;
                    }
                    case ISourceModule source:
                    {
                        var publisher = source.Create(CreateMaterializationContext(effectiveAttributes), out var materialized);
                        AssignPort(source.Shape.Outlets.First(), publisher);
                        materializedValues.Add(atomic, materialized);
                        break;
                    }
                    case IProcessorModule module:
                    {
                        var (subscriber, publisher, materialized) = module.CreateUntypedProcessor();

                        AssignPort(module.In, subscriber);
                        AssignPort(module.Out, publisher);
                        materializedValues.Add(atomic, materialized);
                        break;
                    }
                    //else if (atomic is TlsModule)
                    //{
                    //})
                    case GraphModule graphModule:
                        MaterializeGraph(graphModule, effectiveAttributes, materializedValues);
                        break;
                    case GraphStageModule stage:
                    {
                        var graph =
                            new GraphModule(
                                GraphAssembly.Create(stage.Shape.Inlets, stage.Shape.Outlets, new[] {stage.Stage}),
                                stage.Shape, stage.Attributes, new IModule[] { stage });
                        MaterializeGraph(graph, effectiveAttributes, materializedValues);
                        break;
                    }
                }

                return NotUsed.Instance;
            }

            private string StageName(Attributes attr) => $"{_flowName}-{_nextId++}-{attr.GetNameOrDefault()}";

            private MaterializationContext CreateMaterializationContext(Attributes effectiveAttributes)
                => new(_materializer, effectiveAttributes, StageName(effectiveAttributes));

            private void MaterializeGraph(GraphModule graph, Attributes effectiveAttributes, IDictionary<IModule, object> materializedValues)
            {
                var calculatedSettings = _materializer.EffectiveSettings(effectiveAttributes);
                var t = graph.Assembly.Materialize(effectiveAttributes, graph.MaterializedValueIds, materializedValues, RegisterSource, _materializer);
                var connections = t.Item1;
                var logics = t.Item2;

                var shell = new GraphInterpreterShell(graph.Assembly, connections, logics, graph.Shape, calculatedSettings, _materializer);
                var impl = _subflowFuser != null && !effectiveAttributes.Contains<Attributes.AsyncBoundary>()
                    ? _subflowFuser(shell)
                    : _materializer.ActorOf(ActorGraphInterpreter.Props(shell), StageName(effectiveAttributes), calculatedSettings.Dispatcher);

                var i = 0;
                var inletsEnumerator = graph.Shape.Inlets.GetEnumerator();
                while (inletsEnumerator.MoveNext())
                {
                    var inlet = inletsEnumerator.Current;
                    AssignPort(inlet, inlet.CreateBoundarySubscriber(impl, shell, i));
                    i++;
                }

                i = 0;
                var outletsEnumerator = graph.Shape.Outlets.GetEnumerator();
                while (outletsEnumerator.MoveNext())
                {
                    var outlet = outletsEnumerator.Current;
                    var publisher = outlet.CreateBoundaryPublisher(impl, shell, i, out var actorPublisher);
                    var message = new ActorGraphInterpreter.ExposedPublisher(shell, i, actorPublisher);
                    impl.Tell(message);
                    AssignPort(outlet, publisher);
                    i++;
                }
            }
        }

        #endregion

        private readonly ActorSystem _system;
        private readonly ActorMaterializerSettings _settings;
        private readonly Dispatchers _dispatchers;
        private readonly IActorRef _supervisor;
        private readonly AtomicBoolean _haveShutDown;
        private readonly EnumerableActorName _flowNames;
        private ILoggingAdapter _logger;
        
        public ActorMaterializerImpl(ActorSystem system, ActorMaterializerSettings settings, Dispatchers dispatchers, IActorRef supervisor, AtomicBoolean haveShutDown, EnumerableActorName flowNames)
        {
            _system = system;
            _settings = settings;
            _dispatchers = dispatchers;
            _supervisor = supervisor;
            _haveShutDown = haveShutDown;
            _flowNames = flowNames;

            _executionContext = new Lazy<MessageDispatcher>(() => _dispatchers.Lookup(_settings.Dispatcher == Deploy.NoDispatcherGiven
                ? Dispatchers.DefaultDispatcherId
                : _settings.Dispatcher));

            if (_settings.IsFuzzingMode && !_system.Settings.Config.HasPath("akka.stream.secret-test-fuzzing-warning-disable"))
                Logger.Warning("Fuzzing mode is enabled on this system. If you see this warning on your production system then set 'akka.materializer.debug.fuzzing-mode' to off.");
        }

        /// <summary>
        /// Gets whether the materializer has been shut down or its supervisor has stopped.
        /// </summary>
        public override bool IsShutdown => _haveShutDown.Value;

        /// <summary>
        /// Gets the materializer settings used as defaults for streams.
        /// </summary>
        public override ActorMaterializerSettings Settings => _settings;

        /// <summary>
        /// Gets the actor system that owns this materializer.
        /// </summary>
        public override ActorSystem System => _system;

        /// <summary>
        /// INTERNAL API
        /// </summary>
        [InternalApi]
        public override IActorRef Supervisor => _supervisor;

        /// <summary>
        /// INTERNAL API
        /// </summary>
        [InternalApi]
        public override ILoggingAdapter Logger => _logger ??= GetLogger();

        /// <summary>
        /// Returns a materializer copy whose generated flow names use the supplied prefix.
        /// </summary>
        /// <param name="name">The prefix applied to names generated for materialized flows.</param>
        /// <returns>A materializer sharing this materializer's shutdown state and settings with the requested name prefix.</returns>
        public override IMaterializer WithNamePrefix(string name)
            => new ActorMaterializerImpl(_system, _settings, _dispatchers, _supervisor, _haveShutDown, _flowNames.Copy(name));

        private string CreateFlowName() => _flowNames.Next();

        private Attributes DefaultInitialAttributes =>
            Attributes.CreateInputBuffer(_settings.InitialInputBufferSize, _settings.MaxInputBufferSize)
                .And(ActorAttributes.CreateDispatcher(_settings.Dispatcher))
                .And(ActorAttributes.CreateSupervisionStrategy(_settings.SupervisionDecider));

        /// <summary>
        /// Applies supported stream attributes to the materializer's default settings.
        /// </summary>
        /// <param name="attributes">The effective attributes to apply.</param>
        /// <returns>Settings with input-buffer, dispatcher, and supervision values overridden by matching attributes.</returns>
        public override ActorMaterializerSettings EffectiveSettings(Attributes attributes)
        {
            return attributes.AttributeList.Aggregate(Settings, (settings, attribute) =>
            {
                return attribute switch
                {
                    Attributes.InputBuffer buffer => settings.WithInputBuffer(buffer.Initial, buffer.Max),
                    ActorAttributes.Dispatcher dispatcher => settings.WithDispatcher(dispatcher.Name),
                    ActorAttributes.SupervisionStrategy strategy => settings.WithSupervisionStrategy(strategy.Decider),
                    _ => settings
                };
            });
        }

        /// <summary>
        /// Schedules an action once using the actor system scheduler.
        /// </summary>
        /// <param name="delay">The time to wait before running the action.</param>
        /// <param name="action">The action to run.</param>
        /// <returns>A handle that can cancel the scheduled action.</returns>
        public override ICancelable ScheduleOnce(TimeSpan delay, Action action)
            => _system.Scheduler.Advanced.ScheduleOnceCancelable(delay, action);

        /// <summary>
        /// Schedules an action repeatedly using the actor system scheduler.
        /// </summary>
        /// <param name="initialDelay">The delay before the first execution.</param>
        /// <param name="interval">The interval between executions.</param>
        /// <param name="action">The action to run at each interval.</param>
        /// <returns>A handle that can cancel the repeated schedule.</returns>
        public override ICancelable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action)
            => _system.Scheduler.Advanced.ScheduleRepeatedlyCancelable(initialDelay, interval, action);

        /// <summary>
        /// Materializes a closed graph with the materializer's default initial attributes.
        /// </summary>
        /// <typeparam name="TMat">The materialized value type of the graph.</typeparam>
        /// <param name="runnable">The closed graph to materialize.</param>
        /// <returns>The graph's materialized value.</returns>
        public override TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable) => Materialize(runnable, null,
            DefaultInitialAttributes);

        /// <summary>
        /// Materializes a closed graph using an optional actor factory for eligible subflow shells and default attributes.
        /// </summary>
        /// <typeparam name="TMat">The materialized value type of the graph.</typeparam>
        /// <param name="runnable">The closed graph to materialize.</param>
        /// <param name="subFlowFuser">Creates an actor for eligible subflow shells when no async boundary is present.</param>
        /// <returns>The graph's materialized value.</returns>
        public override TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Func<GraphInterpreterShell, IActorRef> subFlowFuser) 
            => Materialize(runnable, subFlowFuser, DefaultInitialAttributes);

        /// <summary>
        /// Materializes a closed graph with the supplied initial attributes and no subflow actor factory.
        /// </summary>
        /// <typeparam name="TMat">The materialized value type of the graph.</typeparam>
        /// <param name="runnable">The closed graph to materialize.</param>
        /// <param name="initialAttributes">The attributes inherited by the graph.</param>
        /// <returns>The graph's materialized value.</returns>
        public override TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Attributes initialAttributes) =>
            Materialize(runnable, null, initialAttributes);
        
        /// <summary>
        /// Applies configured auto-fusing when enabled, then materializes the graph with the requested initial attributes.
        /// </summary>
        /// <typeparam name="TMat">The materialized value type of the graph.</typeparam>
        /// <param name="runnable">The closed graph to materialize.</param>
        /// <param name="subFlowFuser">Creates actors for eligible subflow shells.</param>
        /// <param name="initialAttributes">The attributes inherited by the graph.</param>
        /// <exception cref="IllegalStateException">The materializer has already been shut down.</exception>
        /// <returns>The graph's materialized value.</returns>
        public override TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Func<GraphInterpreterShell, IActorRef> subFlowFuser, Attributes initialAttributes)
        {
            var runnableGraph = _settings.IsAutoFusing
                ? Fusing.Fusing.Aggressive(runnable)
                : runnable;

            if (_haveShutDown.Value)
                throw new IllegalStateException("Attempted to call Materialize() after the ActorMaterializer has been shut down.");

            if (StreamLayout.IsDebug)
#pragma warning disable CS0162 // Unreachable code detected
                StreamLayout.Validate(runnableGraph.Module);
#pragma warning restore CS0162 // Unreachable code detected

            var session = new ActorMaterializerSession(this, runnableGraph.Module, initialAttributes, subFlowFuser);

            var matVal = session.Materialize();
            return (TMat) matVal;
        }

        /// <summary>
        /// Creates a new logging adapter.
        /// </summary>
        /// <param name="logSource">The source that produces the log events.</param>
        /// <returns>The newly created logging adapter.</returns>
        public override ILoggingAdapter MakeLogger(object logSource)
        {
            string actorPath;
            LogSource newSource;
            if (logSource is not LogSource s)
            {
                actorPath = $"{logSource}({LogSource.FromActorRef(_supervisor, System)})";
                newSource = LogSource.Create(actorPath, logSource.GetType());
                return Logging.GetLogger(System, newSource);
            }
            
            actorPath = $"{s.Source}({LogSource.FromActorRef(_supervisor, System)})";
            newSource = LogSource.Create(actorPath, s.Type);
            return Logging.GetLogger(System, newSource);
        }

        /// <summary>
        /// Gets the dispatcher used as the materializer's execution context.
        /// </summary>
        public override MessageDispatcher ExecutionContext => _executionContext.Value;

        private readonly Lazy<MessageDispatcher> _executionContext;

        /// <summary>
        /// Marks this materializer shut down and sends a stop message to its supervisor once.
        /// </summary>
        public override void Shutdown()
        {
            if (_haveShutDown.CompareAndSet(false, true))
                Supervisor.Tell(PoisonPill.Instance);
        }

        private ILoggingAdapter GetLogger() => MakeLogger(_supervisor);
    }

    /// <summary>
    /// Materializer facade used to materialize subflows into a registered interpreter shell.
    /// </summary>
    public class SubFusingActorMaterializerImpl : IMaterializer
    {
        private readonly ExtendedActorMaterializer _delegateMaterializer;
        private readonly Func<GraphInterpreterShell, IActorRef> _registerShell;

        /// <summary>
        /// Creates a subflow materializer that delegates graph operations and registers fused shells.
        /// </summary>
        /// <param name="delegateMaterializer">The actor materializer that performs graph materialization.</param>
        /// <param name="registerShell">Creates or registers an actor for each eligible graph interpreter shell.</param>
        public SubFusingActorMaterializerImpl(ExtendedActorMaterializer delegateMaterializer, Func<GraphInterpreterShell, IActorRef> registerShell)
        {
            _delegateMaterializer = delegateMaterializer;
            _registerShell = registerShell;
        }

        /// <summary>
        /// Returns a subflow materializer whose generated flow names use the supplied prefix.
        /// </summary>
        /// <param name="namePrefix">The prefix applied to generated flow names.</param>
        /// <returns>A subflow materializer that retains the shell registration callback.</returns>
        public IMaterializer WithNamePrefix(string namePrefix)
            => new SubFusingActorMaterializerImpl((ActorMaterializerImpl) _delegateMaterializer.WithNamePrefix(namePrefix), _registerShell);

        /// <summary>
        /// Materializes a graph and supplies the shell-registration callback used when its effective attributes have no async boundary.
        /// </summary>
        /// <typeparam name="TMat">The materialized value type of the graph.</typeparam>
        /// <param name="runnable">The closed graph to materialize.</param>
        /// <returns>The graph's materialized value.</returns>
        public TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable)
            => _delegateMaterializer.Materialize(runnable, _registerShell);

        /// <summary>
        /// Materializes a graph with the supplied initial attributes and supplies the shell-registration callback when effective attributes have no async boundary.
        /// </summary>
        /// <typeparam name="TMat">The materialized value type of the graph.</typeparam>
        /// <param name="runnable">The closed graph to materialize.</param>
        /// <param name="initialAttributes">The attributes inherited by the graph.</param>
        /// <returns>The graph's materialized value.</returns>
        public TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Attributes initialAttributes) =>
            _delegateMaterializer.Materialize(runnable, _registerShell, initialAttributes);

        /// <summary>
        /// Schedules an action once through the delegated materializer.
        /// </summary>
        /// <param name="delay">The time to wait before running the action.</param>
        /// <param name="action">The action to run.</param>
        /// <returns>A handle that can cancel the scheduled action.</returns>
        public ICancelable ScheduleOnce(TimeSpan delay, Action action)
            => _delegateMaterializer.ScheduleOnce(delay, action);

        /// <summary>
        /// Schedules an action repeatedly through the delegated materializer.
        /// </summary>
        /// <param name="initialDelay">The delay before the first execution.</param>
        /// <param name="interval">The interval between executions.</param>
        /// <param name="action">The action to run at each interval.</param>
        /// <returns>A handle that can cancel the repeated schedule.</returns>
        public ICancelable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action)
            => _delegateMaterializer.ScheduleRepeatedly(initialDelay, interval, action);

        /// <summary>
        /// Gets the execution context provided by the delegated materializer.
        /// </summary>
        public MessageDispatcher ExecutionContext => _delegateMaterializer.ExecutionContext;
    }

    /// <summary>
    /// Actor-system extension that exposes an atomic counter.
    /// </summary>
    public class FlowNameCounter : ExtensionIdProvider<FlowNameCounter>, IExtension
    {
        /// <summary>
        /// Gets or creates this extension for the actor system.
        /// </summary>
        /// <param name="system">The actor system that owns the extension.</param>
        /// <returns>The actor system's counter extension.</returns>
        public static FlowNameCounter Instance(ActorSystem system)
            => system.WithExtension<FlowNameCounter, FlowNameCounter>();

        /// <summary>
        /// Gets the counter exposed by this extension.
        /// </summary>
        public readonly AtomicCounterLong Counter = new(0);

        /// <summary>
        /// Creates a new flow-name counter extension instance.
        /// </summary>
        /// <param name="system">The actor system receiving the extension.</param>
        /// <returns>A new counter extension for that system.</returns>
        public override FlowNameCounter CreateExtension(ExtendedActorSystem system) => new();
    }

    /// <summary>
    /// Supervises materialized stream actors and provides child-management operations.
    /// </summary>
    public class StreamSupervisor : ActorBase
    {
        #region Messages

        /// <summary>
        /// Requests the supervisor to create a child actor with the specified properties and name.
        /// </summary>
        public sealed class Materialize : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// Gets the properties used to create the child actor.
            /// </summary>
            public readonly Props Props;

            /// <summary>
            /// Gets the requested child actor name.
            /// </summary>
            public readonly string Name;

            /// <summary>
            /// Creates a child-materialization request.
            /// </summary>
            /// <param name="props">The properties for the child actor.</param>
            /// <param name="name">The child actor name.</param>
            public Materialize(Props props, string name)
            {
                Props = props;
                Name = name;
            }
        }
        /// <summary>
        /// Requests the current set of child actor references.
        /// </summary>
        public sealed class GetChildren
        {
            /// <summary>
            /// Gets the shared child-query message.
            /// </summary>
            public static readonly GetChildren Instance = new();
            private GetChildren() { }
        }
        /// <summary>
        /// Requests the supervisor to stop all current children.
        /// </summary>
        public sealed class StopChildren
        {
            /// <summary>
            /// Gets the shared stop-children message.
            /// </summary>
            public static readonly StopChildren Instance = new();
            private StopChildren() { }
        }
        /// <summary>
        /// Acknowledges that stop requests have been sent to the supervisor's children.
        /// </summary>
        public sealed class StoppedChildren
        {
            /// <summary>
            /// Gets the shared stop-request acknowledgement.
            /// </summary>
            public static readonly StoppedChildren Instance = new();
            private StoppedChildren() { }
        }
        /// <summary>
        /// Requests a debug dump from a graph interpreter actor.
        /// </summary>
        public sealed class PrintDebugDump
        {
            /// <summary>
            /// Gets the shared debug-dump request.
            /// </summary>
            public static readonly PrintDebugDump Instance = new();
            private PrintDebugDump() { }
        }
        /// <summary>
        /// Carries a snapshot of the supervisor's child actor references.
        /// </summary>
        public sealed class Children
        {
            /// <summary>
            /// Gets the set of child actor references captured by the query.
            /// </summary>
            public readonly IImmutableSet<IActorRef> Refs;
            /// <summary>
            /// Creates a child-reference snapshot.
            /// </summary>
            /// <param name="refs">The child actor references returned by the supervisor.</param>
            public Children(IImmutableSet<IActorRef> refs)
            {
                Refs = refs;
            }
        }

        #endregion

        /// <summary>
        /// Creates local actor properties for the stream supervisor.
        /// </summary>
        /// <param name="settings">The materializer settings stored on the supervisor.</param>
        /// <param name="haveShutdown">The shutdown flag updated when the supervisor stops.</param>
        /// <returns>Local actor properties for creating the supervisor.</returns>
        public static Props Props(ActorMaterializerSettings settings, AtomicBoolean haveShutdown)
            => Actor.Props.Create<StreamSupervisor>(settings, haveShutdown).WithDeploy(Deploy.Local);

        /// <summary>
        /// Generates the next stream-supervisor actor name.
        /// </summary>
        /// <returns>A unique name from the supervisor name sequence.</returns>
        public static string NextName() => ActorName.Next();

        private static readonly EnumerableActorName ActorName = new EnumerableActorNameImpl("StreamSupervisor", new AtomicCounterLong(0L));

        /// <summary>
        /// Gets the materializer settings stored by this supervisor.
        /// </summary>
        public readonly ActorMaterializerSettings Settings;
        /// <summary>
        /// Gets the shared materializer shutdown flag.
        /// </summary>
        public readonly AtomicBoolean HaveShutdown;

        /// <summary>
        /// Creates a stream supervisor with the supplied settings and shutdown state.
        /// </summary>
        /// <param name="settings">The settings stored by this supervisor.</param>
        /// <param name="haveShutdown">The shared flag set when this supervisor stops.</param>
        /// If this changes you must also change StreamSupervisor.Props as well!
        public StreamSupervisor(ActorMaterializerSettings settings, AtomicBoolean haveShutdown)
        {
            Settings = settings;
            HaveShutdown = haveShutdown;
        }

        /// <summary>
        /// Stops child actors when they fail rather than restarting them.
        /// </summary>
        /// <returns>The stopping supervision strategy.</returns>
        protected override SupervisorStrategy SupervisorStrategy() => Actor.SupervisorStrategy.StoppingStrategy;

        /// <summary>
        /// Creates requested actors, reports children, and stops current children.
        /// </summary>
        /// <param name="message">The supervisor command to handle.</param>
        /// <returns><see langword="true"/> for a recognized command; otherwise <see langword="false"/>.</returns>
        protected override bool Receive(object message)
        {
            if (message is Materialize materialize)
            {
                Sender.Tell(Context.ActorOf(materialize.Props, materialize.Name));
            }
            else if (message is GetChildren)
                Sender.Tell(new Children(Context.GetChildren().ToImmutableHashSet()));
            else if (message is StopChildren)
            {
                foreach (var child in Context.GetChildren())
                    Context.Stop(child);

                Sender.Tell(StoppedChildren.Instance);
            }
            else
                return false;
            return true;
        }

        /// <summary>
        /// Marks the shared shutdown flag when the supervisor stops.
        /// </summary>
        protected override void PostStop() => HaveShutdown.Value = true;
    }
}
