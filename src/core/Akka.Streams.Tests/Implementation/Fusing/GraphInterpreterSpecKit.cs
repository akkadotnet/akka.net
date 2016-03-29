using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.TestKit.Tests;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Implementation.Fusing
{
    public class GraphInterpreterSpecKit : AkkaSpec
    {
        public GraphInterpreterSpecKit(ITestOutputHelper output = null) : base(output)
        {
        }

        public abstract class BaseBuilder
        {
            private GraphInterpreter _interpreter;
            private readonly ILoggingAdapter _logger;

            protected BaseBuilder(ActorSystem system)
            {
                _logger = Logging.GetLogger(system, "InterpreterSpecKit");
            }

            public GraphInterpreter Interpreter => _interpreter;

            public void StepAll()
            {
                Interpreter.Execute(int.MaxValue);
            }

            public void Step()
            {
                Interpreter.Execute(1);
            }

            public class Upstream : GraphInterpreter.UpstreamBoundaryStageLogic
            {
                private readonly Outlet<int> _out;

                public Upstream()
                {
                    _out = new Outlet<int>("up") { Id = 0 };
                }

                public override Outlet Out => _out;
            }

            public class Downstream : GraphInterpreter.DownstreamBoundaryStageLogic
            {
                private readonly Inlet<int> _in;

                public Downstream()
                {
                    _in = new Inlet<int>("up") { Id = 0 };
                }

                public override Inlet In => _in;
            }

            public class AssemblyBuilder
            {
                private readonly ILoggingAdapter _logger;
                private readonly Action<GraphInterpreter> _interpreterSetter;
                private readonly IList<IGraphStageWithMaterializedValue> _stages;

                private readonly IList<Tuple<GraphInterpreter.UpstreamBoundaryStageLogic, Inlet>> _upstreams =
                    new List<Tuple<GraphInterpreter.UpstreamBoundaryStageLogic, Inlet>>();
                private readonly IList<Tuple<Outlet, GraphInterpreter.DownstreamBoundaryStageLogic>> _downstreams =
                    new List<Tuple<Outlet, GraphInterpreter.DownstreamBoundaryStageLogic>>();
                private readonly IList<Tuple<Outlet, Inlet>> _connections = new List<Tuple<Outlet, Inlet>>();

                public AssemblyBuilder(ILoggingAdapter logger, Action<GraphInterpreter> interpreterSetter, IEnumerable<IGraphStageWithMaterializedValue> stages)
                {
                    _logger = logger;
                    _interpreterSetter = interpreterSetter;
                    _stages = stages.ToArray();
                }

                public AssemblyBuilder Connect<T>(GraphInterpreter.UpstreamBoundaryStageLogic upstream, Inlet<T> inlet)
                {
                    _upstreams.Add(new Tuple<GraphInterpreter.UpstreamBoundaryStageLogic, Inlet>(upstream, inlet));
                    return this;
                }

                public AssemblyBuilder Connect<T>(Outlet<T> outlet, GraphInterpreter.DownstreamBoundaryStageLogic downstream)
                {
                    _downstreams.Add(new Tuple<Outlet, GraphInterpreter.DownstreamBoundaryStageLogic>(outlet, downstream));
                    return this;
                }

                public AssemblyBuilder Connect<T>(Outlet<T> outlet, Inlet<T> inlet)
                {
                    _connections.Add(new Tuple<Outlet, Inlet>(outlet, inlet));
                    return this;
                }

                public GraphAssembly BuildAssembly()
                {
                    var ins = _upstreams.Select(u => u.Item2).Concat(_connections.Select(c => c.Item2)).ToArray();
                    var outs = _connections.Select(c => c.Item1).Concat(_downstreams.Select(d => d.Item1)).ToArray();
                    var inOwners =
                        ins.Select(
                            inlet =>
                                _stages.Select((s, i) => new {Stage = s, Index = i})
                                    .First(s => s.Stage.Shape.Inlets.Contains(inlet))
                                    .Index).ToArray();
                    var outOwners =
                        outs.Select(
                            outlet =>
                                _stages.Select((s, i) => new {Stage = s, Index = i})
                                    .First(s => s.Stage.Shape.Outlets.Contains(outlet))
                                    .Index);

                    return new GraphAssembly(_stages.ToArray(),
                        Enumerable.Repeat(Attributes.None, _stages.Count).ToArray(),
                        ins.Concat(Enumerable.Repeat<Inlet>(null, _downstreams.Count)).ToArray(),
                        inOwners.Concat(Enumerable.Repeat(-1, _downstreams.Count)).ToArray(),
                        Enumerable.Repeat<Outlet>(null, _upstreams.Count).Concat(outs).ToArray(),
                        Enumerable.Repeat(-1, _upstreams.Count).Concat(outOwners).ToArray());
                }

                public void Init()
                {
                    var assembly = BuildAssembly();

                    var mat = assembly.Materialize(Attributes.None, assembly.Stages.Select(s => s.Module).ToArray(),
                        new Dictionary<IModule, object>(), s => { });
                    var inHandlers = mat.Item1;
                    var outHandlers = mat.Item2;
                    var logics = mat.Item3;
                    var interpreter = new GraphInterpreter(assembly, NoMaterializer.Instance, _logger, inHandlers, outHandlers, logics, (l, o, a) => {}, false);

                    var i = 0;
                    foreach (var upstream in _upstreams)
                    {
                        interpreter.AttachUpstreamBoundary(i++, upstream.Item1);
                    }
                    i = 0;
                    foreach (var downstream in _downstreams)
                    {
                        interpreter.AttachDownstreamBoundary((i++) + _upstreams.Count + _connections.Count, downstream.Item2);
                    }
                    interpreter.Init(null);
                    _interpreterSetter(interpreter);
                }
            }

            public void ManualInit(GraphAssembly assembly)
            {
                var mat = assembly.Materialize(Attributes.None, assembly.Stages.Select(s => s.Module).ToArray(),
                    new Dictionary<IModule, object>(), s => { });
                var inHandlers = mat.Item1;
                var outHandlers = mat.Item2;
                var logics = mat.Item3;
                _interpreter = new GraphInterpreter(assembly, NoMaterializer.Instance, _logger, inHandlers, outHandlers, logics, (l, o, a) => {}, false);
            }

            public AssemblyBuilder Builder(params IGraphStageWithMaterializedValue[] stages)
            {
                return new AssemblyBuilder(_logger, (interpreter) => _interpreter = interpreter, stages);
            }
        }

        public class BaseBuilderSetup<T> : BaseBuilder
        {
            public BaseBuilderSetup(ActorSystem system) : base(system)
            {

            }

            public GraphInterpreter Build(GraphInterpreter.UpstreamBoundaryStageLogic upstream, GraphStage<FlowShape<T, T>>[] ops, GraphInterpreter.DownstreamBoundaryStageLogic downstream)
            {
                var b = Builder(ops).Connect(upstream, ops[0].Shape.Inlet);
                for (var i = 0; i < ops.Length - 1; i++)
                    b.Connect(ops[i].Shape.Outlet, ops[i + 1].Shape.Inlet);
                b.Connect(ops[ops.Length - 1].Shape.Outlet, downstream);
                b.Init();

                return Interpreter;
            }
        }

        public class TestSetup : BaseBuilder
        {
            #region Test Events
            public interface ITestEvent
            {
                GraphStageLogic Source { get; }
            }

            public class OnComplete : ITestEvent
            {
                public GraphStageLogic Source { get; }

                public OnComplete(GraphStageLogic source)
                {
                    Source = source;
                }

                protected bool Equals(OnComplete other)
                {
                    return Equals(Source, other.Source);
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((OnComplete) obj);
                }

                public override int GetHashCode()
                {
                    return Source?.GetHashCode() ?? 0;
                }
            }

            public class Cancel : ITestEvent
            {
                public GraphStageLogic Source { get; }

                public Cancel(GraphStageLogic source)
                {
                    Source = source;
                }

                protected bool Equals(Cancel other)
                {
                    return Equals(Source, other.Source);
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((Cancel) obj);
                }

                public override int GetHashCode()
                {
                    return Source?.GetHashCode() ?? 0;
                }
            }

            public class OnError : ITestEvent
            {
                public GraphStageLogic Source { get; }
                public Exception Cause { get; }

                public OnError(GraphStageLogic source, Exception cause)
                {
                    Source = source;
                    Cause = cause;
                }

                protected bool Equals(OnError other)
                {
                    return Equals(Source, other.Source) && Equals(Cause, other.Cause);
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((OnError) obj);
                }

                public override int GetHashCode()
                {
                    unchecked
                    {
                        return ((Source?.GetHashCode() ?? 0)*397) ^ (Cause?.GetHashCode() ?? 0);
                    }
                }
            }

            public class OnNext : ITestEvent
            {
                public GraphStageLogic Source { get; }
                public object Element { get; }

                public OnNext(GraphStageLogic source, object element)
                {
                    Source = source;
                    Element = element;
                }

                protected bool Equals(OnNext other)
                {
                    return Equals(Source, other.Source) && Equals(Element, other.Element);
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((OnNext) obj);
                }

                public override int GetHashCode()
                {
                    unchecked
                    {
                        return ((Source?.GetHashCode() ?? 0)*397) ^ (Element?.GetHashCode() ?? 0);
                    }
                }
            }

            public class RequestOne : ITestEvent
            {
                public GraphStageLogic Source { get; }

                public RequestOne(GraphStageLogic source)
                {
                    Source = source;
                }

                protected bool Equals(RequestOne other)
                {
                    return Equals(Source, other.Source);
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((RequestOne) obj);
                }

                public override int GetHashCode()
                {
                    return Source?.GetHashCode() ?? 0;
                }
            }

            public class RequestAnother : ITestEvent
            {
                public GraphStageLogic Source { get; }

                public RequestAnother(GraphStageLogic source)
                {
                    Source = source;
                }

                protected bool Equals(RequestAnother other)
                {
                    return Equals(Source, other.Source);
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((RequestAnother) obj);
                }

                public override int GetHashCode()
                {
                    return Source?.GetHashCode() ?? 0;
                }
            }

            public class PreStart : ITestEvent
            {
                public GraphStageLogic Source { get; }

                public PreStart(GraphStageLogic source)
                {
                    Source = source;
                }

                protected bool Equals(PreStart other)
                {
                    return Equals(Source, other.Source);
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((PreStart) obj);
                }

                public override int GetHashCode()
                {
                    return Source?.GetHashCode() ?? 0;
                }
            }

            public class PostStop : ITestEvent
            {
                public GraphStageLogic Source { get; }

                public PostStop(GraphStageLogic source)
                {
                    Source = source;
                }

                protected bool Equals(PostStop other)
                {
                    return Equals(Source, other.Source);
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((PostStop) obj);
                }

                public override int GetHashCode()
                {
                    return Source?.GetHashCode() ?? 0;
                }
            }
            #endregion

            public TestSetup(ActorSystem system) : base(system)
            {
            }

            public ISet<ITestEvent> LastEvent = new HashSet<ITestEvent>();

            public ISet<ITestEvent> LastEvents()
            {
                var result = LastEvent;
                ClearEvents();
                return result;
            }

            public void ClearEvents()
            {
                LastEvent = new HashSet<ITestEvent>();
            }

            public UpstreamProbe<T> NewUpstreamProbe<T>(string name)
            {
                return new UpstreamProbe<T>(this, name);
            }

            public DownstreamProbe<T> NewDownstreamProbe<T>(string name)
            {
                return new DownstreamProbe<T>(this, name);
            }

            public class UpstreamProbe<T> : GraphInterpreter.UpstreamBoundaryStageLogic
            {
                private readonly string _name;

                public UpstreamProbe(TestSetup setup, string name)
                {
                    _name = name;
                    Out = new Outlet<T>("out") {Id = 0};

                    var probe = this;
                    SetHandler(Out, () => setup.LastEvent.Add(new RequestOne(probe)), () => setup.LastEvent.Add(new Cancel(probe)));
                }

                public sealed override Outlet Out { get; }

                public void OnNext(T element, int eventLimit = int.MaxValue)
                {
                    if (GraphInterpreter.IsDebug)
                        Console.WriteLine($"----- NEXT: {this} {element}");
                    Push(Out, element);
                    Interpreter.Execute(eventLimit);
                }

                public void OnComplete(int eventLimit = int.MaxValue)
                {
                    if (GraphInterpreter.IsDebug)
                        Console.WriteLine($"----- COMPLETE: {this}");
                    Complete(Out);
                    Interpreter.Execute(eventLimit);
                }

                public void OnFailure(int eventLimit = int.MaxValue, Exception ex = null)
                {
                    if (GraphInterpreter.IsDebug)
                        Console.WriteLine($"----- FAIL: {this}");
                    Fail(Out, ex);
                    Interpreter.Execute(eventLimit);
                }

                public override string ToString()
                {
                    return _name;
                }
            }

            public class DownstreamProbe<T> : GraphInterpreter.DownstreamBoundaryStageLogic
            {
                private readonly string _name;

                public DownstreamProbe(TestSetup setup, string name)
                {
                    _name = name;
                    In = new Inlet<T>("in") {Id = 0};

                    var probe = this;
                    SetHandler(In, () => setup.LastEvent.Add(new OnNext(probe, Grab<T>(In))),
                        () => setup.LastEvent.Add(new OnComplete(probe)),
                        ex => setup.LastEvent.Add(new OnError(probe, ex)));
                }

                public sealed override Inlet In { get; }

                public void RequestOne(int eventLimit = int.MaxValue)
                {
                    if (GraphInterpreter.IsDebug)
                        Console.WriteLine($"----- REQ: {this}");
                    Pull<T>(In);
                    Interpreter.Execute(eventLimit);
                }

                public void Cancel(int eventLimit = int.MaxValue)
                {
                    if (GraphInterpreter.IsDebug)
                        Console.WriteLine($"----- CANCEL: {this}");
                    Cancel(In);
                    Interpreter.Execute(eventLimit);
                }

                public override string ToString()
                {
                    return _name;
                }
            }
        }

        public class PortTestSetup : TestSetup
        {
            public UpstreamPortProbe<int> Out { get; }
            public DownstreamPortProbe<int> In { get; }

            private readonly GraphAssembly _assembly = new GraphAssembly(new IGraphStageWithMaterializedValue[0],
                new Attributes[0], new Inlet[] {null}, new[] {-1}, new Outlet[] {null}, new[] {-1});

            public PortTestSetup(ActorSystem system) : base(system)
            {
                Out = new UpstreamPortProbe<int>(this);
                In = new DownstreamPortProbe<int>(this);

                ManualInit(_assembly);
                Interpreter.AttachDownstreamBoundary(0, In);
                Interpreter.AttachUpstreamBoundary(0, Out);
                Interpreter.Init(null);
            }

            public class UpstreamPortProbe<T> : UpstreamProbe<T>
            {
                public UpstreamPortProbe(TestSetup setup) : base(setup, "upstreamPort")
                {
                }

                public bool IsAvailable()
                {
                    return IsAvailable(Out);
                }

                public bool IsClosed()
                {
                    return IsClosed(Out);
                }

                public void Push(T element)
                {
                    Push(Out, element);
                }

                public void Complete()
                {
                    Complete(Out);
                }

                public void Fail(Exception ex)
                {
                    Fail(Out, ex);
                }
            }

            public class DownstreamPortProbe<T> : DownstreamProbe<T>
            {
                public DownstreamPortProbe(TestSetup setup) : base(setup, "downstreamPort")
                {
                    var probe = this;
                    SetHandler(In, () =>
                    {
                        // Modified onPush that does not Grab() automatically the element. This access some internals.
                        var internalEvent = Interpreter.ConnectionSlots[PortToConn[In.Id]];

                        if (internalEvent is GraphInterpreter.Failed)
                            ((PortTestSetup) setup).LastEvent.Add(new OnNext(probe,
                                ((GraphInterpreter.Failed) internalEvent).PreviousElement));
                        else
                            ((PortTestSetup) setup).LastEvent.Add(new OnNext(probe, internalEvent));
                    },
                        () => ((PortTestSetup) setup).LastEvent.Add(new OnComplete(probe)),
                        ex => ((PortTestSetup) setup).LastEvent.Add(new OnError(probe, ex))
                        );
                }

                public bool IsAvailable()
                {
                    return IsAvailable(In);
                }

                public bool HasBeenPulled()
                {
                    return HasBeenPulled(In);
                }

                public bool IsClosed()
                {
                    return IsClosed(In);
                }

                public void Pull()
                {
                    Pull<T>(In);
                }

                public void Cancel()
                {
                    Cancel(In);
                }

                public T Grab()
                {
                    return Grab<T>(In);
                }
            }
        }

        public class FailingStageSetup : TestSetup
        {
            public UpstreamPortProbe<int> Upstream { get; }
            public DownstreamPortProbe<int> Downstream { get; }

            private bool _failOnNextEvent;
            private bool _failOnPostStop;
            private readonly Inlet<int> _stageIn;
            private readonly Outlet<int> _stageOut;
            private readonly FlowShape<int, int> _stageShape;
            private GraphStage<FlowShape<int, int>> _sandwitchStage;

            // Must be lazy because I turned this stage "inside-out" therefore changin initialization order
            // to make tests a bit more readable
            public Lazy<GraphStageLogic> Stage { get; }

            public FailingStageSetup(ActorSystem system, bool initFailOnNextEvent = false) : base(system)
            {
                Upstream = new UpstreamPortProbe<int>(this);
                Downstream = new DownstreamPortProbe<int>(this);

                _failOnNextEvent = initFailOnNextEvent;
                _failOnPostStop = false;

                _stageIn = new Inlet<int>("sandwitch.in");
                _stageOut = new Outlet<int>("sandwitch.out");
                _stageShape = new FlowShape<int, int>(_stageIn, _stageOut);

                Stage = new Lazy<GraphStageLogic>(() => new FailingGraphStageLogic(this, _stageShape));

                _sandwitchStage = new SandwitchStage(this);

                Builder(_sandwitchStage)
                    .Connect(Upstream, _stageIn)
                    .Connect(_stageOut, Downstream)
                    .Init();
            }

            public void FailOnNextEvent()
            {
                _failOnNextEvent = true;
            }

            public void FailOnPostStop()
            {
                _failOnPostStop = true;
            }

            public Exception TestException()
            {
                return new TestException("test");
            }

            public class FailingGraphStageLogic : GraphStageLogic
            {
                private readonly FailingStageSetup _setup;

                public FailingGraphStageLogic(FailingStageSetup setup, Shape shape) : base(shape)
                {
                    _setup = setup;

                    SetHandler(setup._stageIn,
                        () => MayFail(() => Push(setup._stageOut, Grab(setup._stageIn))),
                        () => MayFail(CompleteStage),
                        ex => MayFail(() => FailStage(ex)));

                    SetHandler(setup._stageOut,
                        () => MayFail(() => Pull(setup._stageIn)),
                        () => MayFail(CompleteStage));
                }

                private void MayFail(Action task)
                {
                    if (!_setup._failOnNextEvent)
                        task();
                    else
                    {
                        _setup._failOnNextEvent = false;
                        throw _setup.TestException();
                    }
                }

                public override void PreStart()
                {
                    MayFail(() => _setup.LastEvent.Add(new PreStart(this)));
                }

                public override void PostStop()
                {
                    if (!_setup._failOnPostStop)
                        _setup.LastEvent.Add(new PostStop(this));
                    else throw _setup.TestException();
                }

                public override string ToString()
                {
                    return "stage";
                }
            }

            public class SandwitchStage : GraphStage<FlowShape<int, int>>
            {
                private readonly FailingStageSetup _setup;

                public SandwitchStage(FailingStageSetup setup)
                {
                    _setup = setup;
                }

                public override FlowShape<int, int> Shape => _setup._stageShape;

                protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
                {
                    return _setup.Stage.Value;
                }

                public override string ToString()
                {
                    return "stage";
                }
            }

            public class UpstreamPortProbe<T> : UpstreamProbe<T>
            {
                public UpstreamPortProbe(TestSetup setup) : base(setup, "upstreamPort")
                {
                }

                public void Push(T element)
                {
                    Push(Out, element);
                }

                public void Complete()
                {
                    Complete(Out);
                }

                public void Fail(Exception ex)
                {
                    Fail(Out, ex);
                }
            }

            public class DownstreamPortProbe<T> : DownstreamProbe<T>
            {
                public DownstreamPortProbe(TestSetup setup) : base(setup, "downstreamPort")
                {
                }

                public void Pull()
                {
                    Pull<T>(In);
                }

                public void Cancel()
                {
                    Cancel(In);
                }
            }
        }

        public abstract class OneBoundedSetup : BaseBuilder
        {
            #region Test Events
            public interface ITestEvent
            {
            }

            public class OnComplete : ITestEvent
            {
                protected bool Equals(OnComplete other)
                {
                    return true;
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != typeof (OnComplete)) return false;
                    return Equals((OnComplete) obj);
                }

                public override int GetHashCode()
                {
                    return 0;
                }
            }

            public class Cancel : ITestEvent
            {
                protected bool Equals(Cancel other)
                {
                    return true;
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != typeof (Cancel)) return false;
                    return Equals((Cancel) obj);
                }

                public override int GetHashCode()
                {
                    return 0;
                }
            }

            public class OnError : ITestEvent
            {
                public Exception Cause { get; }

                public OnError(Exception cause)
                {
                    Cause = cause;
                }

                protected bool Equals(OnError other)
                {
                    return Equals(Cause, other.Cause);
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((OnError) obj);
                }

                public override int GetHashCode()
                {
                    return Cause?.GetHashCode() ?? 0;
                }
            }

            public class OnNext : ITestEvent
            {
                public object Element { get; }

                public OnNext(object element)
                {
                    Element = element;
                }

                protected bool Equals(OnNext other)
                {
                    return (Element is IEnumerable)
                        ? ((IEnumerable) Element).Cast<object>().SequenceEqual(((IEnumerable) other.Element).Cast<object>())
                        : Equals(Element, other.Element);
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((OnNext) obj);
                }

                public override int GetHashCode()
                {
                    return Element?.GetHashCode() ?? 0;
                }
            }

            public class RequestOne : ITestEvent
            {
                protected bool Equals(RequestOne other)
                {
                    return true;
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != typeof (RequestOne)) return false;
                    return Equals((RequestOne) obj);
                }

                public override int GetHashCode()
                {
                    return 0;
                }
            }

            public class RequestAnother : ITestEvent
            {
                protected bool Equals(RequestAnother other)
                {
                    return true;
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != typeof (RequestAnother)) return false;
                    return Equals((RequestAnother) obj);
                }

                public override int GetHashCode()
                {
                    return 0;
                }
            }
            #endregion

            protected OneBoundedSetup(ActorSystem system) : base(system)
            {
            }

            protected ISet<ITestEvent> LastEvent { get; private set; } = new HashSet<ITestEvent>();

            public ISet<ITestEvent> LastEvents()
            {
                var events = LastEvent;
                LastEvent = new HashSet<ITestEvent>();
                return events;
            }

            protected abstract void Run();

            public class UpstreamOneBoundedProbe<T> : GraphInterpreter.UpstreamBoundaryStageLogic
            {
                private readonly OneBoundedSetup _setup;

                public UpstreamOneBoundedProbe(OneBoundedSetup setup)
                {
                    _setup = setup;
                    Out = new Outlet<T>("out") {Id = 0};

                    SetHandler(Out, () =>
                    {
                        if (setup.LastEvent.OfType<RequestOne>().Any())
                            setup.LastEvent.Add(new RequestAnother());
                        else
                            setup.LastEvent.Add(new RequestOne());
                    }, () => setup.LastEvent.Add(new Cancel()));
                }

                public override Outlet Out { get; }

                public void OnNext(T element)
                {
                    Push(Out, element);
                    _setup.Run();
                }

                public void OnComplete()
                {
                    Complete(Out);
                    _setup.Run();
                }

                public void OnNextAndComplete(T element)
                {
                    Push(Out, element);
                    Complete(Out);
                    _setup.Run();
                }

                public void OnError(Exception ex)
                {
                    Fail(Out, ex);
                    _setup.Run();
                }
            }

            public class DownstreamOneBoundedPortProbe<T> : GraphInterpreter.DownstreamBoundaryStageLogic
            {
                private readonly OneBoundedSetup _setup;

                public DownstreamOneBoundedPortProbe(OneBoundedSetup setup)
                {
                    _setup = setup;
                    In = new Inlet<T>("in") {Id = 0};

                    SetHandler(In, () =>
                    {
                        setup.LastEvent.Add(new OnNext(Grab<object>(In)));
                    },
                    () => setup.LastEvent.Add(new OnComplete()),
                    ex => setup.LastEvent.Add(new OnError(ex)));
                }

                public override Inlet In { get; }

                public void RequestOne()
                {
                    Pull<T>(In);
                    _setup.Run();
                }

                public void Cancel()
                {
                    Cancel(In);
                    _setup.Run();
                }
            }
        }

        public class OneBoundedSetup<TIn, TOut> : OneBoundedSetup
        {
            public OneBoundedSetup(ActorSystem system, params IGraphStageWithMaterializedValue[] ops) : base(system)
            {
                Ops = ops;
                Upstream = new UpstreamOneBoundedProbe<TIn>(this);
                Downstream = new DownstreamOneBoundedPortProbe<TOut>(this);

                Initialize();
                Run(); // Detached stages need the prefetch
            }

            public IGraphStageWithMaterializedValue[] Ops { get; }
            public new UpstreamOneBoundedProbe<TIn> Upstream { get; }
            public new DownstreamOneBoundedPortProbe<TOut> Downstream { get; }

            protected sealed override void Run()
            {
                Interpreter.Execute(int.MaxValue);
            }

            private void Initialize()
            {
                var attributes = Enumerable.Repeat(Attributes.None, Ops.Length).ToArray();
                var ins = new Inlet[Ops.Length + 1];
                var inOwners = new int[Ops.Length + 1];
                var outs = new Outlet[Ops.Length + 1];
                var outOwners = new int[Ops.Length + 1];

                ins[Ops.Length] = null;
                inOwners[Ops.Length] = GraphInterpreter.Boundary;
                outs[0] = null;
                outOwners[0] = GraphInterpreter.Boundary;

                for (int i = 0; i < Ops.Length; i++)
                {
                    var shape = (IFlowShape) Ops[i].Shape;
                    ins[i] = shape.Inlet;
                    inOwners[i] = i;
                    outs[i + 1] = shape.Outlet;
                    outOwners[i + 1] = i;
                }

                ManualInit(new GraphAssembly(Ops, attributes, ins, inOwners, outs, outOwners));
                Interpreter.AttachUpstreamBoundary(0, Upstream);
                Interpreter.AttachDownstreamBoundary(Ops.Length, Downstream);

                Interpreter.Init(null);
            }
        }

        public class OneBoundedSetup<T> : OneBoundedSetup<T, T>
        {
            public OneBoundedSetup(ActorSystem system, params IGraphStageWithMaterializedValue[] ops) : base(system, ops)
            {
            }
        }

        public PushPullGraphStage<TIn, TOut> ToGraphStage<TIn, TOut>(IStage<TIn, TOut> stage)
        {
            var s = stage;
            return new PushPullGraphStage<TIn, TOut>(_ => s, Attributes.None);
        }

        public IGraphStageWithMaterializedValue[] ToGraphStage<TIn, TOut>(IStage<TIn, TOut>[] stages)
        {
            return stages.Select(ToGraphStage).Cast<IGraphStageWithMaterializedValue>().ToArray();
        }

        public void WithTestSetup(Action<TestSetup, Func<ISet<TestSetup.ITestEvent>>> spec)
        {
            var setup = new TestSetup(Sys);
            spec(setup, setup.LastEvents);
        }

        public void WithTestSetup(
            Action
                <TestSetup, Func<IGraphStageWithMaterializedValue, BaseBuilder.AssemblyBuilder>,
                    Func<ISet<TestSetup.ITestEvent>>> spec)
        {
            var setup = new TestSetup(Sys);
            spec(setup, g => setup.Builder(g), setup.LastEvents);
        }

        public void WithTestSetup(
            Action
                <TestSetup, Func<IGraphStageWithMaterializedValue[], BaseBuilder.AssemblyBuilder>,
                    Func<ISet<TestSetup.ITestEvent>>> spec)
        {
            var setup = new TestSetup(Sys);
            spec(setup, setup.Builder, setup.LastEvents);
        }

        public void WithOneBoundedSetup<T>(IStage<T, T> op,
            Action
                <Func<ISet<OneBoundedSetup.ITestEvent>>, OneBoundedSetup.UpstreamOneBoundedProbe<T>,
                    OneBoundedSetup.DownstreamOneBoundedPortProbe<T>> spec)
        {
            WithOneBoundedSetup<T>(ToGraphStage(op), spec);
        }

        public void WithOneBoundedSetup<T>(IStage<T, T>[] ops,
            Action
                <Func<ISet<OneBoundedSetup.ITestEvent>>, OneBoundedSetup.UpstreamOneBoundedProbe<T>,
                    OneBoundedSetup.DownstreamOneBoundedPortProbe<T>> spec)
        {
            WithOneBoundedSetup<T>(ToGraphStage(ops), spec);
        }

        public void WithOneBoundedSetup<T>(IGraphStageWithMaterializedValue op,
            Action
                <Func<ISet<OneBoundedSetup.ITestEvent>>, OneBoundedSetup.UpstreamOneBoundedProbe<T>,
                    OneBoundedSetup.DownstreamOneBoundedPortProbe<T>>
                spec)
        {
            WithOneBoundedSetup<T>(new[] {op}, spec);
            
        }

        public void WithOneBoundedSetup<T>(IGraphStageWithMaterializedValue[] ops,
            Action
                <Func<ISet<OneBoundedSetup.ITestEvent>>, OneBoundedSetup.UpstreamOneBoundedProbe<T>, OneBoundedSetup.DownstreamOneBoundedPortProbe<T>>
                spec)
        {
            var setup = new OneBoundedSetup<T>(Sys, ops);
            spec(setup.LastEvents, setup.Upstream, setup.Downstream);
        }

        public void WithOneBoundedSetup<TIn, TOut>(IGraphStageWithMaterializedValue op,
            Action
                <Func<ISet<OneBoundedSetup.ITestEvent>>, OneBoundedSetup.UpstreamOneBoundedProbe<TIn>,
                    OneBoundedSetup.DownstreamOneBoundedPortProbe<TOut>>
                spec)
        {
            WithOneBoundedSetup(new[] {op}, spec);
            
        }

        public void WithOneBoundedSetup<TIn, TOut>(IGraphStageWithMaterializedValue[] ops,
            Action
                <Func<ISet<OneBoundedSetup.ITestEvent>>, OneBoundedSetup.UpstreamOneBoundedProbe<TIn>, OneBoundedSetup.DownstreamOneBoundedPortProbe<TOut>>
                spec)
        {
            var setup = new OneBoundedSetup<TIn, TOut>(Sys, ops);
            spec(setup.LastEvents, setup.Upstream, setup.Downstream);
        }

        public void WithBaseBuilderSetup<T>(GraphStage<FlowShape<T, T>>[] ops, Action<GraphInterpreter> spec)
        {
            var interpreter = new BaseBuilderSetup<T>(Sys).Build(new BaseBuilder.Upstream(), ops, new BaseBuilder.Downstream());
            spec(interpreter);
        }
    }
}