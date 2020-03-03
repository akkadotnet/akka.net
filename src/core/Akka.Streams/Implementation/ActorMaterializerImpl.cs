//-----------------------------------------------------------------------
// <copyright file="ActorMaterializerImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
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
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="runnable">TBD</param>
        /// <param name="subFlowFuser">TBD</param>
        /// <returns>TBD</returns>
        [InternalApi]
        public abstract TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Func<GraphInterpreterShell, IActorRef> subFlowFuser);

        /// <summary>
        /// INTERNAL API
        /// </summary>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="runnable">TBD</param>
        /// <param name="subFlowFuser">TBD</param>
        /// <param name="initialAttributes">TBD</param>
        /// <returns>TBD</returns>
        [InternalApi]
        public abstract TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Func<GraphInterpreterShell, IActorRef> subFlowFuser, Attributes initialAttributes);

        /// <summary>
        /// INTERNAL API
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="props">TBD</param>
        /// <returns>TBD</returns>
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
        /// <param name="props">TBD</param>
        /// <param name="name">TBD</param>
        /// <param name="dispatcher">TBD</param>
        /// <exception cref="IllegalStateException">TBD</exception>
        /// <returns>TBD</returns>
        [InternalApi]
        protected IActorRef ActorOf(Props props, string name, string dispatcher)
        {
            var localActorRef = Supervisor as LocalActorRef;
            if (localActorRef != null)
                return ((ActorCell) localActorRef.Underlying).AttachChild(props.WithDispatcher(dispatcher),
                    isSystemService: false, name: name);


            var repointableActorRef = Supervisor as RepointableActorRef;
            if (repointableActorRef != null)
            {
                if (repointableActorRef.IsStarted)
                    return ((ActorCell)repointableActorRef.Underlying).AttachChild(props.WithDispatcher(dispatcher), isSystemService: false, name: name);

                var timeout = repointableActorRef.Underlying.System.Settings.CreationTimeout;
                var f = repointableActorRef.Ask<IActorRef>(new StreamSupervisor.Materialize(props.WithDispatcher(dispatcher), name), timeout);
                return f.Result;
            }

            throw new IllegalStateException($"Stream supervisor must be a local actor, was [{Supervisor.GetType()}]");
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class ActorMaterializerImpl : ExtendedActorMaterializer
    {
        #region Materializer session implementation

        private sealed class ActorMaterializerSession : MaterializerSession
        {
            private static readonly MethodInfo ProcessorForMethod =
                typeof(ActorMaterializerSession).GetMethod("ProcessorFor",
                    BindingFlags.NonPublic | BindingFlags.Instance);
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

                if (atomic is ISinkModule)
                {
                    var sink = (ISinkModule) atomic;
                    object materialized;
                    var subscriber = sink.Create(CreateMaterializationContext(effectiveAttributes), out materialized);
                    AssignPort(sink.Shape.Inlets.First(), subscriber);
                    materializedValues.Add(atomic, materialized);
                }
                else if (atomic is ISourceModule)
                {
                    var source = (ISourceModule) atomic;
                    object materialized;
                    var publisher = source.Create(CreateMaterializationContext(effectiveAttributes), out materialized);
                    AssignPort(source.Shape.Outlets.First(), publisher);
                    materializedValues.Add(atomic, materialized);
                }
                else if (atomic is IProcessorModule)
                {
                    var stage = atomic as IProcessorModule;
                    var t = stage.CreateProcessor();
                    var processor = t.Item1;
                    var materialized = t.Item2;

                    AssignPort(stage.In, UntypedSubscriber.FromTyped(processor));
                    AssignPort(stage.Out, UntypedPublisher.FromTyped(processor));
                    materializedValues.Add(atomic, materialized);
                }
                //else if (atomic is TlsModule)
                //{
                //})
                else if (atomic is GraphModule)
                {
                    var graph = (GraphModule) atomic;
                    MaterializeGraph(graph, effectiveAttributes, materializedValues);
                }
                else if (atomic is GraphStageModule)
                {
                    var stage = (GraphStageModule) atomic;
                    var graph =
                        new GraphModule(
                            GraphAssembly.Create(stage.Shape.Inlets, stage.Shape.Outlets, new[] {stage.Stage}),
                            stage.Shape, stage.Attributes, new IModule[] {stage});
                    MaterializeGraph(graph, effectiveAttributes, materializedValues);
                }

                return NotUsed.Instance;
            }

            private string StageName(Attributes attr) => $"{_flowName}-{_nextId++}-{attr.GetNameOrDefault()}";

            private MaterializationContext CreateMaterializationContext(Attributes effectiveAttributes)
                => new MaterializationContext(_materializer, effectiveAttributes, StageName(effectiveAttributes));

            private void MaterializeGraph(GraphModule graph, Attributes effectiveAttributes, IDictionary<IModule, object> materializedValues)
            {
                var calculatedSettings = _materializer.EffectiveSettings(effectiveAttributes);
                var t = graph.Assembly.Materialize(effectiveAttributes, graph.MaterializedValueIds, materializedValues, RegisterSource);
                var connections = t.Item1;
                var logics = t.Item2;

                var shell = new GraphInterpreterShell(graph.Assembly, connections, logics, graph.Shape, calculatedSettings, _materializer);
                var impl = _subflowFuser != null && !effectiveAttributes.Contains(Attributes.AsyncBoundary.Instance)
                    ? _subflowFuser(shell)
                    : _materializer.ActorOf(ActorGraphInterpreter.Props(shell), StageName(effectiveAttributes), calculatedSettings.Dispatcher);

                var i = 0;
                var inletsEnumerator = graph.Shape.Inlets.GetEnumerator();
                while (inletsEnumerator.MoveNext())
                {
                    var inlet = inletsEnumerator.Current;
                    var elementType = inlet.GetType().GetGenericArguments().First();
                    var subscriber = typeof(ActorGraphInterpreter.BoundarySubscriber<>).Instantiate(elementType, impl, shell, i);
                    AssignPort(inlet, UntypedSubscriber.FromTyped(subscriber));
                    i++;
                }

                i = 0;
                var outletsEnumerator = graph.Shape.Outlets.GetEnumerator();
                while (outletsEnumerator.MoveNext())
                {
                    var outlet = outletsEnumerator.Current;
                    var elementType = outlet.GetType().GetGenericArguments().First();
                    var publisher = typeof(ActorGraphInterpreter.BoundaryPublisher<>).Instantiate(elementType, impl, shell, i);
                    var message = new ActorGraphInterpreter.ExposedPublisher(shell, i, (IActorPublisher)publisher);
                    impl.Tell(message);
                    AssignPort(outletsEnumerator.Current, (IUntypedPublisher) publisher);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="dispatchers">TBD</param>
        /// <param name="supervisor">TBD</param>
        /// <param name="haveShutDown">TBD</param>
        /// <param name="flowNames">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        public override bool IsShutdown => _haveShutDown.Value;

        /// <summary>
        /// TBD
        /// </summary>
        public override ActorMaterializerSettings Settings => _settings;

        /// <summary>
        /// TBD
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
        public override ILoggingAdapter Logger => _logger ?? (_logger = GetLogger());

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public override IMaterializer WithNamePrefix(string name)
            => new ActorMaterializerImpl(_system, _settings, _dispatchers, _supervisor, _haveShutDown, _flowNames.Copy(name));

        private string CreateFlowName() => _flowNames.Next();

        private Attributes DefaultInitialAttributes =>
            Attributes.CreateInputBuffer(_settings.InitialInputBufferSize, _settings.MaxInputBufferSize)
                .And(ActorAttributes.CreateDispatcher(_settings.Dispatcher))
                .And(ActorAttributes.CreateSupervisionStrategy(_settings.SupervisionDecider));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override ActorMaterializerSettings EffectiveSettings(Attributes attributes)
        {
            return attributes.AttributeList.Aggregate(Settings, (settings, attribute) =>
            {
                var buffer = attribute as Attributes.InputBuffer;
                if (buffer != null)
                    return settings.WithInputBuffer(buffer.Initial, buffer.Max);

                var dispatcher = attribute as ActorAttributes.Dispatcher;
                if (dispatcher != null)
                    return settings.WithDispatcher(dispatcher.Name);
                
                var strategy = attribute as ActorAttributes.SupervisionStrategy;
                if (strategy != null)
                    return settings.WithSupervisionStrategy(strategy.Decider);

                return settings;
            });
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="action">TBD</param>
        /// <returns>TBD</returns>
        public override ICancelable ScheduleOnce(TimeSpan delay, Action action)
            => _system.Scheduler.Advanced.ScheduleOnceCancelable(delay, action);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="action">TBD</param>
        /// <returns>TBD</returns>
        public override ICancelable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action)
            => _system.Scheduler.Advanced.ScheduleRepeatedlyCancelable(initialDelay, interval, action);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="runnable">TBD</param>
        /// <returns>TBD</returns>
        public override TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable) => Materialize(runnable, null,
            DefaultInitialAttributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="runnable">TBD</param>
        /// <param name="subFlowFuser">TBD</param>
        /// <returns>TBD</returns>
        public override TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Func<GraphInterpreterShell, IActorRef> subFlowFuser) 
            => Materialize(runnable, subFlowFuser, DefaultInitialAttributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="runnable">TBD</param>
        /// <param name="initialAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Attributes initialAttributes) =>
            Materialize(runnable, null, initialAttributes);
        
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="runnable">TBD</param>
        /// <param name="subFlowFuser">TBD</param>
        /// <param name="initialAttributes">TBD</param>
        /// <exception cref="IllegalStateException">TBD</exception>
        /// <returns>TBD</returns>
        public override TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Func<GraphInterpreterShell, IActorRef> subFlowFuser, Attributes initialAttributes)
        {
            var runnableGraph = _settings.IsAutoFusing
                ? Fusing.Fusing.Aggressive(runnable)
                : runnable;

            if (_haveShutDown.Value)
                throw new IllegalStateException("Attempted to call Materialize() after the ActorMaterializer has been shut down.");

            if (StreamLayout.IsDebug)
                StreamLayout.Validate(runnableGraph.Module);

            var session = new ActorMaterializerSession(this, runnableGraph.Module, initialAttributes, subFlowFuser);

            var matVal = session.Materialize();
            return (TMat) matVal;
        }

        /// <summary>
        /// Creates a new logging adapter.
        /// </summary>
        /// <param name="logSource">The source that produces the log events.</param>
        /// <returns>The newly created logging adapter.</returns>
        public override ILoggingAdapter MakeLogger(object logSource) => Logging.GetLogger(System, logSource);

        /// <summary>
        /// TBD
        /// </summary>
        public override MessageDispatcher ExecutionContext => _executionContext.Value;

        private readonly Lazy<MessageDispatcher> _executionContext;

        /// <summary>
        /// TBD
        /// </summary>
        public override void Shutdown()
        {
            if (_haveShutDown.CompareAndSet(false, true))
                Supervisor.Tell(PoisonPill.Instance);
        }

        private ILoggingAdapter GetLogger() => _system.Log;
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class SubFusingActorMaterializerImpl : IMaterializer
    {
        private readonly ExtendedActorMaterializer _delegateMaterializer;
        private readonly Func<GraphInterpreterShell, IActorRef> _registerShell;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delegateMaterializer">TBD</param>
        /// <param name="registerShell">TBD</param>
        public SubFusingActorMaterializerImpl(ExtendedActorMaterializer delegateMaterializer, Func<GraphInterpreterShell, IActorRef> registerShell)
        {
            _delegateMaterializer = delegateMaterializer;
            _registerShell = registerShell;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="namePrefix">TBD</param>
        /// <returns>TBD</returns>
        public IMaterializer WithNamePrefix(string namePrefix)
            => new SubFusingActorMaterializerImpl((ActorMaterializerImpl) _delegateMaterializer.WithNamePrefix(namePrefix), _registerShell);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="runnable">TBD</param>
        /// <returns>TBD</returns>
        public TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable)
            => _delegateMaterializer.Materialize(runnable, _registerShell);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="runnable">TBD</param>
        /// <param name="initialAttributes">TBD</param>
        /// <returns>TBD</returns>
        public TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Attributes initialAttributes) =>
            _delegateMaterializer.Materialize(runnable, _registerShell, initialAttributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="action">TBD</param>
        /// <returns>TBD</returns>
        public ICancelable ScheduleOnce(TimeSpan delay, Action action)
            => _delegateMaterializer.ScheduleOnce(delay, action);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="action">TBD</param>
        /// <returns>TBD</returns>
        public ICancelable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action)
            => _delegateMaterializer.ScheduleRepeatedly(initialDelay, interval, action);

        /// <summary>
        /// TBD
        /// </summary>
        public MessageDispatcher ExecutionContext => _delegateMaterializer.ExecutionContext;
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class FlowNameCounter : ExtensionIdProvider<FlowNameCounter>, IExtension
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static FlowNameCounter Instance(ActorSystem system)
            => system.WithExtension<FlowNameCounter, FlowNameCounter>();

        /// <summary>
        /// TBD
        /// </summary>
        public readonly AtomicCounterLong Counter = new AtomicCounterLong(0);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override FlowNameCounter CreateExtension(ExtendedActorSystem system) => new FlowNameCounter();
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class StreamSupervisor : ActorBase
    {
        #region Messages

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Materialize : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly Props Props;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly string Name;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="props">TBD</param>
            /// <param name="name">TBD</param>
            public Materialize(Props props, string name)
            {
                Props = props;
                Name = name;
            }
        }
        /// <summary>
        /// TBD
        /// </summary>
        public sealed class GetChildren
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly GetChildren Instance = new GetChildren();
            private GetChildren() { }
        }
        /// <summary>
        /// TBD
        /// </summary>
        public sealed class StopChildren
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly StopChildren Instance = new StopChildren();
            private StopChildren() { }
        }
        /// <summary>
        /// TBD
        /// </summary>
        public sealed class StoppedChildren
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly StoppedChildren Instance = new StoppedChildren();
            private StoppedChildren() { }
        }
        /// <summary>
        /// TBD
        /// </summary>
        public sealed class PrintDebugDump
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly PrintDebugDump Instance = new PrintDebugDump();
            private PrintDebugDump() { }
        }
        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Children
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IImmutableSet<IActorRef> Refs;
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="refs">TBD</param>
            public Children(IImmutableSet<IActorRef> refs)
            {
                Refs = refs;
            }
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="settings">TBD</param>
        /// <param name="haveShutdown">TBD</param>
        /// <returns>TBD</returns>
        public static Props Props(ActorMaterializerSettings settings, AtomicBoolean haveShutdown)
            => Actor.Props.Create(() => new StreamSupervisor(settings, haveShutdown)).WithDeploy(Deploy.Local);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static string NextName() => ActorName.Next();

        private static readonly EnumerableActorName ActorName = new EnumerableActorNameImpl("StreamSupervisor", new AtomicCounterLong(0L));

        /// <summary>
        /// TBD
        /// </summary>
        public readonly ActorMaterializerSettings Settings;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly AtomicBoolean HaveShutdown;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="settings">TBD</param>
        /// <param name="haveShutdown">TBD</param>
        public StreamSupervisor(ActorMaterializerSettings settings, AtomicBoolean haveShutdown)
        {
            Settings = settings;
            HaveShutdown = haveShutdown;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        protected override SupervisorStrategy SupervisorStrategy() => Actor.SupervisorStrategy.StoppingStrategy;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            if (message is Materialize)
            {
                var materialize = (Materialize) message;
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
        /// TBD
        /// </summary>
        protected override void PostStop() => HaveShutdown.Value = true;
    }
}
