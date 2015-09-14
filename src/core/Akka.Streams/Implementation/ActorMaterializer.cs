//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using Akka.Actor;
//using Akka.Dispatch;
//using Akka.Pattern;
//using Akka.Util;
//using Akka.Util.Internal;

//namespace Akka.Streams.Implementation
//{
//    public sealed class ActorMaterializer : BaseActorMaterializer
//    {
//        private readonly ActorSystem _system;
//        private readonly ActorMaterializerSettings _settings;
//        private readonly Dispatchers _dispatchers;
//        private readonly IActorRef _supervisor;
//        private readonly AtomicCounterLong _flowNameCounter;
//        private readonly string _namePrefix;
//        private readonly Optimizations _optimizations;

//        public ActorMaterializer(ActorSystem system, ActorMaterializerSettings settings, Dispatchers dispatchers, IActorRef supervisor, AtomicCounterLong flowNameCounter,
//            string namePrefix, Optimizations optimizations)
//        {
//            _system = system;
//            _settings = settings;
//            _dispatchers = dispatchers;
//            _supervisor = supervisor;
//            _flowNameCounter = flowNameCounter;
//            _namePrefix = namePrefix;
//            _optimizations = optimizations;
//        }

//        public override ActorMaterializerSettings Settings { get { return _settings; } }
//        protected override ActorSystem System { get { return _system; } }

//        public override Materializer WithNamePrefix(string name)
//        {
//            return new ActorMaterializer(_system, _settings, _dispatchers, _supervisor, _flowNameCounter, name, _optimizations);
//        }

//        public override TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable)
//        {
//            throw new System.NotImplementedException();
//        }

//        protected override IActorRef ActorOf(MaterializationContext context, Props props)
//        {
//            var dispatcher = props.Dispatcher == Deploy.NoDispatcherGiven
//                ? EffectiveSettings(context.OperationAttributes).Dispatcher
//                : props.Dispatcher;

//            return ActorOf(props, context.StageName, dispatcher).Result; //TODO: fix Result
//        }

//        public ActorMaterializerSettings EffectiveSettings(Attributes attributes)
//        {
//            return attributes.Attributes.Aggregate(_settings, (settings, attribute) =>
//            {
//                if (attribute is Attributes.InputBuffer)
//                {
//                    var inputBuffer = attribute as Attributes.InputBuffer;
//                    return settings.WithInputBuffer(inputBuffer.Initial, inputBuffer.Max);
//                }
//                else if (attribute is ActorOperationAttributes.Dispatcher)
//                {
//                    return settings.WithDispatcher((attribute as ActorOperationAttributes.Dispatcher).Name);
//                }
//                else if (attribute is ActorOperationAttributes.SupervisionStrategy)
//                {
//                    return settings.WithSupervisionStrategy((attribute as ActorOperationAttributes.SupervisionStrategy).Decider);
//                }
//                else return settings;
//            });
//        }

//        private async Task<IActorRef> ActorOf(Props props, string name, string dispatcher)
//        {
//            if (_supervisor is LocalActorRef)
//            {
//                var local = _supervisor as LocalActorRef;
//                var cell = (ActorCell)local.Underlying;
//                return cell.AttachChild(props.WithDispatcher(dispatcher), false, name);
//            }
//            else if (_supervisor is RepointableActorRef)
//            {
//                var repointable = _supervisor as RepointableActorRef;
//                var cell = (ActorCell)repointable.Underlying;
//                if (repointable.IsStarted)
//                {
//                    return cell.AttachChild(props.WithDispatcher(dispatcher), false, name);
//                }
//                else
//                {
//                    var timeout = cell.System.Settings.CreationTimeout;
//                    var task = _supervisor.Ask<IActorRef>(new StreamSupervisor.Materialize(props.WithDispatcher(dispatcher), name), timeout);
//                    return await task;
//                }
//            }
//            else
//            {
//                throw new IllegalStateException("Stream supervisor must be local actor, was " + _supervisor);
//            }
//        }

//        private long NextFlowNameCount()
//        {
//            return _flowNameCounter.IncrementAndGet();
//        }

//        private string CreateFlowName()
//        {
//            return _namePrefix + "-" + NextFlowNameCount();
//        }
//    }

//    public class FlowNameCounter : ExtensionIdProvider<FlowNameCounter>, IExtension
//    {
//        public readonly AtomicCounterLong Counter = new AtomicCounterLong(0L);

//        public static FlowNameCounter Get(ActorSystem system)
//        {
//            return system.WithExtension<FlowNameCounter, FlowNameCounter>();
//        }

//        public override FlowNameCounter CreateExtension(ExtendedActorSystem system)
//        {
//            return new FlowNameCounter();
//        }
//    }
    

//    public class StreamSupervisor : ActorBase
//    {
//        #region Messages

//        public sealed class Materialize
//        {
//            public readonly Props Props;
//            public readonly string Name;

//            public Materialize(Props props, string name)
//            {
//                Props = props;
//                Name = name;
//            }
//        }

//        public sealed class Children
//        {
//            public readonly IEnumerable<IActorRef> ChildrenCollection;

//            public Children(IEnumerable<IActorRef> childrenCollection)
//            {
//                ChildrenCollection = childrenCollection;
//            }
//        }

//        public sealed class GetChildren
//        {
//            public static readonly GetChildren Instance = new GetChildren();
//            private GetChildren() { }
//        }

//        public sealed class StopChildren
//        {
//            public static readonly StopChildren Instance = new StopChildren();
//            private StopChildren() { }
//        }

//        #endregion

//        private readonly ActorMaterializerSettings _settings;

//        public static Props Props(ActorMaterializerSettings settings, AtomicReference<bool> haveShutDown)
//        {
//            return Actor.Props.Create(() => new StreamSupervisor(settings));
//        }

//        private StreamSupervisor(ActorMaterializerSettings settings)
//        {
//            _settings = settings;
//        }

//        protected override bool Receive(object message)
//        {
//            if (message is Materialize)
//            {
//                var materialize = message as Materialize;
//                var impl = Context.ActorOf(materialize.Props, materialize.Name);
//                Sender.Tell(impl);
//            }
//            else if (message is GetChildren)
//            {
//                Sender.Tell(new Children(Context.GetChildren().ToArray()));
//            }
//            else if (message is StopChildren)
//            {
//                foreach (var child in Context.GetChildren())
//                {
//                    Context.Stop(child);
//                }
//            }
//            else return false;
//            return true;
//        }
//    }
//}