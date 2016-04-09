using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Streams;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Streams.Implementation
{
    public sealed class ActorMaterializerImpl : ActorMaterializer
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

            protected override object MaterializeAtomic(IModule atomic, Attributes effectiveAttributes,
                IDictionary<IModule, object> materializedValues)
            {
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
                else if (atomic is StageModule)
                {
                    // FIXME: Remove this, only stream-of-stream ops need it
                    var stage = (StageModule) atomic;
                    object materialized;
                    var processor = ProcessorFor(stage, effectiveAttributes,
                        _materializer.EffectiveSettings(effectiveAttributes), out materialized);
                    AssignPort(stage.In, processor);
                    AssignPort(stage.Out, processor);
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

                return Unit.Instance;
            }

            private string StageName(Attributes attr)
            {
                return $"{_flowName}-{_nextId++}-{attr.GetNameOrDefault()}";
            }

            private MaterializationContext CreateMaterializationContext(Attributes effectiveAttributes)
            {
                return new MaterializationContext(_materializer, effectiveAttributes, StageName(effectiveAttributes));
            }

            private void MaterializeGraph(GraphModule graph, Attributes effectiveAttributes, IDictionary<IModule, object> materializedValues)
            {
                var calculatedSettings = _materializer.EffectiveSettings(effectiveAttributes);
                var t = graph.Assembly.Materialize(effectiveAttributes, graph.MaterializedValueIds, materializedValues, RegisterSource);
                var inHandlers = t.Item1;
                var outHandlers = t.Item2;
                var logics = t.Item3;

                var shell = new GraphInterpreterShell(graph.Assembly, inHandlers, outHandlers, logics, graph.Shape, calculatedSettings, _materializer);
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
                    AssignPort(inlet, (ISubscriber)subscriber);
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
                    AssignPort(outletsEnumerator.Current, (IPublisher)publisher);
                    i++;
                }
            }

            private IProcessor<object, object> ProcessorFor(StageModule op, Attributes effectiveAttributes, ActorMaterializerSettings settings, out object materialized)
            {
                DirectProcessor processor;
                if ((processor = op as DirectProcessor) != null)
                {
                    var t = processor.ProcessorFactory();
                    materialized = t.Item2;
                    return t.Item1;
                }
                else
                {
                    var props = ActorProcessorFactory.Props(_materializer, op, effectiveAttributes, out materialized);
                    return ActorProcessorFactory.Create<object, object>(_materializer.ActorOf(props, StageName(effectiveAttributes), settings.Dispatcher));
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

        public override bool IsShutdown => _haveShutDown.Value;
        public override ActorMaterializerSettings Settings => _settings;
        public override ActorSystem System => _system;
        public override IActorRef Supervisor => _supervisor;
        public override ILoggingAdapter Logger => _logger ?? (_logger = GetLogger());

        public override IMaterializer WithNamePrefix(string name)
        {
            return new ActorMaterializerImpl(_system, _settings, _dispatchers, _supervisor, _haveShutDown, _flowNames.Copy(name));
        }

        private string CreateFlowName()
        {
            return _flowNames.Next();
        }

        private Attributes InitialAttributes =>
            Attributes.CreateInputBuffer(_settings.InitialInputBufferSize, _settings.MaxInputBufferSize)
                .And(ActorAttributes.CreateDispatcher(_settings.Dispatcher))
                .And(ActorAttributes.CreateSupervisionStrategy(_settings.SupervisionDecider));

        public override ActorMaterializerSettings EffectiveSettings(Attributes attributes)
        {
            return attributes.AttributeList.Aggregate(Settings, (settings, attribute) =>
            {
                if (attribute is Attributes.InputBuffer)
                {
                    var inputBuffer = (Attributes.InputBuffer)attribute;
                    return settings.WithInputBuffer(inputBuffer.Initial, inputBuffer.Max);
                }
                if (attribute is ActorAttributes.Dispatcher) return settings.WithDispatcher(((ActorAttributes.Dispatcher)attribute).Name);
                if (attribute is ActorAttributes.SupervisionStrategy) return settings.WithSupervisionStrategy(((ActorAttributes.SupervisionStrategy)attribute).Decider);
                return settings;
            });
        }

        public override ICancelable ScheduleOnce(TimeSpan delay, Action action)
        {
            return _system.Scheduler.Advanced.ScheduleOnceCancelable(delay, action);
        }

        public override ICancelable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            return _system.Scheduler.Advanced.ScheduleRepeatedlyCancelable(initialDelay, interval, action);
        }

        public override TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable)
        {
            return Materialize(runnable, null);
        }

        internal TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Func<GraphInterpreterShell, IActorRef> subFlowFuser)
        {
            var runnableGraph = _settings.IsAutoFusing
                ? Fusing.Fusing.Aggressive(runnable)
                : runnable;

            if (_haveShutDown.Value)
                throw new IllegalStateException("Attempted to call Materialize() after the ActorMaterializer has been shut down.");

            if (StreamLayout.IsDebug) StreamLayout.Validate(runnableGraph.Module);

            var session = new ActorMaterializerSession(this, runnableGraph.Module, InitialAttributes, subFlowFuser);

            var matVal = session.Materialize();
            return (TMat) matVal;
        }

        private readonly Lazy<MessageDispatcher> _executionContext;
        public override MessageDispatcher ExecutionContext => _executionContext.Value;

        public override void Shutdown()
        {
            if (_haveShutDown.CompareAndSet(false, true)) Supervisor.Tell(PoisonPill.Instance);
        }

        protected internal override IActorRef ActorOf(MaterializationContext context, Props props)
        {
            var dispatcher = props.Deploy.Dispatcher == Deploy.NoDispatcherGiven
                ? EffectiveSettings(context.EffectiveAttributes).Dispatcher
                : props.Dispatcher;

            return ActorOf(props, context.StageName, dispatcher);
        }

        private IActorRef ActorOf(Props props, string name, string dispatcher)
        {
            if (Supervisor is LocalActorRef)
            {
                var aref = (LocalActorRef)Supervisor;
                return ((ActorCell)aref.Underlying).AttachChild(props.WithDispatcher(dispatcher), isSystemService: false, name: name);
            }
            if (Supervisor is RepointableActorRef)
            {
                var aref = (RepointableActorRef)Supervisor;
                if (aref.IsStarted)
                    return ((ActorCell)aref.Underlying).AttachChild(props.WithDispatcher(dispatcher), isSystemService: false, name: name);

                var timeout = aref.Underlying.System.Settings.CreationTimeout;
                var f = Supervisor.Ask<IActorRef>(new StreamSupervisor.Materialize(props.WithDispatcher(dispatcher), name), timeout);
                return f.Result;
            }
            throw new IllegalStateException($"Stream supervisor must be a local actor, was [{Supervisor.GetType()}]");
        }

        private ILoggingAdapter GetLogger()
        {
            return _system.Log;
        }
    }

    internal class SubFusingActorMaterializerImpl : IMaterializer
    {
        private readonly ActorMaterializerImpl _delegateMaterializer;
        private readonly Func<GraphInterpreterShell, IActorRef> _registerShell;

        public SubFusingActorMaterializerImpl(ActorMaterializerImpl delegateMaterializer, Func<GraphInterpreterShell, IActorRef> registerShell)
        {
            _delegateMaterializer = delegateMaterializer;
            _registerShell = registerShell;
        }

        public IMaterializer WithNamePrefix(string namePrefix)
        {
            return new SubFusingActorMaterializerImpl((ActorMaterializerImpl)_delegateMaterializer.WithNamePrefix(namePrefix), _registerShell);
        }

        public TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable)
        {
            return _delegateMaterializer.Materialize(runnable, _registerShell);
        }

        public ICancelable ScheduleOnce(TimeSpan delay, Action action)
        {
            return _delegateMaterializer.ScheduleOnce(delay, action);
        }

        public ICancelable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            return _delegateMaterializer.ScheduleRepeatedly(initialDelay, interval, action);
        }

        public MessageDispatcher ExecutionContext => _delegateMaterializer.ExecutionContext;
    }

    internal class FlowNameCounter : ExtensionIdProvider<FlowNameCounter>, IExtension
    {
        public static FlowNameCounter Instance(ActorSystem system)
        {
            return system.WithExtension<FlowNameCounter, FlowNameCounter>();
        }

        public readonly AtomicCounterLong Counter = new AtomicCounterLong(0);

        public override FlowNameCounter CreateExtension(ExtendedActorSystem system)
        {
            return new FlowNameCounter();
        }
    }
    
    public class StreamSupervisor : ActorBase
    {
        #region Messages
        
        public sealed class Materialize : INoSerializationVerificationNeeded
        {
            public readonly Props Props;
            public readonly string Name;

            public Materialize(Props props, string name)
            {
                Props = props;
                Name = name;
            }
        }
        public sealed class GetChildren
        {
            public static readonly GetChildren Instance = new GetChildren();
            private GetChildren() { }
        }
        public sealed class StopChildren
        {
            public static readonly StopChildren Instance = new StopChildren();
            private StopChildren() { }
        }
        public sealed class StoppedChildren
        {
            public static readonly StoppedChildren Instance = new StoppedChildren();
            private StoppedChildren() { }
        }
        public sealed class PrintDebugDump
        {
            public static readonly PrintDebugDump Instance = new PrintDebugDump();
            private PrintDebugDump() { }
        }
        public sealed class Children
        {
            public readonly IImmutableSet<IActorRef> Refs;
            public Children(IImmutableSet<IActorRef> refs)
            {
                Refs = refs;
            }
        }

        #endregion

        public static Props Props(ActorMaterializerSettings settings, AtomicBoolean haveShutdown)
        {
            return Actor.Props.Create(() => new StreamSupervisor(settings, haveShutdown)).WithDeploy(Deploy.Local);
        }

        public static string NextName()
        {
            return ActorName.Next();
        }

        private static readonly EnumerableActorName ActorName = new EnumerableActorNameImpl("StreamSupervisor", new AtomicCounterLong(0L));

        public readonly ActorMaterializerSettings Settings;
        public readonly AtomicBoolean HaveShutdown;

        public StreamSupervisor(ActorMaterializerSettings settings, AtomicBoolean haveShutdown)
        {
            Settings = settings;
            HaveShutdown = haveShutdown;
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return Actor.SupervisorStrategy.StoppingStrategy;
        }

        protected override bool Receive(object message)
        {
            if (message is Materialize)
            {
                var materialize = (Materialize) message;
                Sender.Tell(Context.ActorOf(materialize.Props, materialize.Name));
            }
            else if (message is GetChildren)
            {
                Sender.Tell(new Children(Context.GetChildren().ToImmutableHashSet()));
            }
            else if (message is StopChildren)
            {
                foreach (var child in Context.GetChildren())
                    Context.Stop(child);

                Sender.Tell(StoppedChildren.Instance);
            }
            else return false;
            return true;
        }

        protected override void PostStop()
        {
            HaveShutdown.Value = true;
        }
    }

    internal static class ActorProcessorFactory
    {
        public static Props Props(ActorMaterializer materializer, StageModule op, Attributes parentAttributes, out object materialized)
        {
            var attr = parentAttributes.And(op.Attributes);
            // USE THIS TO AVOID CLOSING OVER THE MATERIALIZER BELOW
            // Also, otherwise the attributes will not affect the settings properly!
            var settings = materializer.EffectiveSettings(attr);    
            Props result = null;
            materialized = null;

            op.Match()
                .With<GroupBy>(groupBy =>
                {
                    result = GroupByProcessorImpl.Props(settings, groupBy.MaxSubstreams, groupBy.Extractor);
                })
                .With<DirectProcessor>(processor =>
                {
                    throw new ArgumentException("DirectProcessor cannot end up in ActorProcessorFactory");
                })
                .Default(_ => { throw new ArgumentException($"StageModule type {op.GetType()} is not supported"); });

            return result;
        }

        public static ActorProcessor<TIn, TOut> Create<TIn, TOut>(IActorRef impl)
        {
            var p = new ActorProcessor<TIn,TOut>(impl);
            // Resolve cyclic dependency with actor. This MUST be the first message no matter what.
            impl.Tell(new ExposedPublisher(p));
            return p;
        }
    }
}