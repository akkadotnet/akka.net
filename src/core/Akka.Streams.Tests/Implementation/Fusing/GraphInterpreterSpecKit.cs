//-----------------------------------------------------------------------
// <copyright file="GraphInterpreterSpecKit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Implementation.Fusing
{
    public class GraphInterpreterSpecKit : AkkaSpec
    {
        public GraphInterpreterSpecKit(ITestOutputHelper output = null, Config config = null) : base(output, config)
        {
        }

        public abstract class BaseBuilder
        {
            private GraphInterpreter _interpreter;
            private readonly ILoggingAdapter _logger;

            protected BaseBuilder(ActorSystem system) => _logger = Logging.GetLogger(system, "InterpreterSpecKit");

            public GraphInterpreter Interpreter => _interpreter;

            public void StepAll() => Interpreter.Execute(int.MaxValue);

            public virtual void Step() => Interpreter.Execute(1);

            public class Upstream : GraphInterpreter.UpstreamBoundaryStageLogic
            {
                public Upstream() => Out = new Outlet<int>("up") { Id = 0 };

                public override Outlet Out { get; }
            }

            public class Downstream : GraphInterpreter.DownstreamBoundaryStageLogic
            {
                public Downstream() => In = new Inlet<int>("up") { Id = 0 };

                public override Inlet In { get; }
            }

            public class AssemblyBuilder
            {
                private readonly ILoggingAdapter _logger;
                private readonly Action<GraphInterpreter> _interpreterSetter;
                private readonly IList<IGraphStageWithMaterializedValue<Shape, object>> _stages;

                private readonly IList<(GraphInterpreter.UpstreamBoundaryStageLogic, Inlet)> _upstreams =
                    new List<(GraphInterpreter.UpstreamBoundaryStageLogic, Inlet)>();
                private readonly IList<(Outlet, GraphInterpreter.DownstreamBoundaryStageLogic)> _downstreams =
                    new List<(Outlet, GraphInterpreter.DownstreamBoundaryStageLogic)>();
                private readonly IList<(Outlet, Inlet)> _connections = new List<(Outlet, Inlet)>();

                public AssemblyBuilder(ILoggingAdapter logger, Action<GraphInterpreter> interpreterSetter, IEnumerable<IGraphStageWithMaterializedValue<Shape, object>> stages)
                {
                    _logger = logger;
                    _interpreterSetter = interpreterSetter;
                    _stages = stages.ToArray();
                }

                public AssemblyBuilder Connect<T>(GraphInterpreter.UpstreamBoundaryStageLogic upstream, Inlet<T> inlet)
                {
                    _upstreams.Add((upstream, (Inlet)inlet));
                    return this;
                }

                public AssemblyBuilder Connect<T>(Outlet<T> outlet, GraphInterpreter.DownstreamBoundaryStageLogic downstream)
                {
                    _downstreams.Add(((Outlet)outlet, downstream));
                    return this;
                }

                public AssemblyBuilder Connect<T>(Outlet<T> outlet, Inlet<T> inlet)
                {
                    _connections.Add(((Outlet)outlet, (Inlet)inlet));
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
                    var connections = mat.Item1;
                    var logics = mat.Item2;
                    var interpreter = new GraphInterpreter(assembly, NoMaterializer.Instance, _logger, logics, connections, (l, o, a) => {}, false, null);

                    var i = 0;
                    foreach (var upstream in _upstreams)
                    {
                        interpreter.AttachUpstreamBoundary(connections[i++], upstream.Item1);
                    }
                    i = 0;
                    foreach (var downstream in _downstreams)
                    {
                        interpreter.AttachDownstreamBoundary(connections[i++ + _upstreams.Count + _connections.Count], downstream.Item2);
                    }
                    interpreter.Init(null);
                    _interpreterSetter(interpreter);
                }
            }

            public void ManualInit(GraphAssembly assembly)
            {
                var mat = assembly.Materialize(Attributes.None, assembly.Stages.Select(s => s.Module).ToArray(),
                    new Dictionary<IModule, object>(), s => { });
                var connections = mat.Item1;
                var logics = mat.Item2;
                _interpreter = new GraphInterpreter(assembly, NoMaterializer.Instance, _logger, logics, connections, (l, o, a) => {}, false, null);
            }

            public AssemblyBuilder Builder(params IGraphStageWithMaterializedValue<Shape, object>[] stages)
            {
                return new AssemblyBuilder(_logger, interpreter => _interpreter = interpreter, stages);
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

                public OnComplete(GraphStageLogic source) => Source = source;

                protected bool Equals(OnComplete other) => Equals(Source, other.Source);

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((OnComplete) obj);
                }

                public override int GetHashCode() => Source?.GetHashCode() ?? 0;
            }

            public class Cancel : ITestEvent
            {
                public GraphStageLogic Source { get; }

                public Cancel(GraphStageLogic source) => Source = source;

                protected bool Equals(Cancel other) => Equals(Source, other.Source);

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((Cancel) obj);
                }

                public override int GetHashCode() => Source?.GetHashCode() ?? 0;
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

                protected bool Equals(OnError other) => Equals(Source, other.Source) && Equals(Cause, other.Cause);

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

                protected bool Equals(OnNext other) => Equals(Source, other.Source) && Equals(Element, other.Element);

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

                public RequestOne(GraphStageLogic source) => Source = source;

                protected bool Equals(RequestOne other) => Equals(Source, other.Source);

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((RequestOne) obj);
                }

                public override int GetHashCode() => Source?.GetHashCode() ?? 0;
            }

            public class RequestAnother : ITestEvent
            {
                public GraphStageLogic Source { get; }

                public RequestAnother(GraphStageLogic source) => Source = source;

                protected bool Equals(RequestAnother other) => Equals(Source, other.Source);

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((RequestAnother) obj);
                }

                public override int GetHashCode() => Source?.GetHashCode() ?? 0;
            }

            public class PreStart : ITestEvent
            {
                public GraphStageLogic Source { get; }

                public PreStart(GraphStageLogic source) => Source = source;

                protected bool Equals(PreStart other) => Equals(Source, other.Source);

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((PreStart) obj);
                }

                public override int GetHashCode() => Source?.GetHashCode() ?? 0;
            }

            public class PostStop : ITestEvent
            {
                public GraphStageLogic Source { get; }

                public PostStop(GraphStageLogic source) => Source = source;

                protected bool Equals(PostStop other) => Equals(Source, other.Source);

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((PostStop) obj);
                }

                public override int GetHashCode() => Source?.GetHashCode() ?? 0;
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

            public void ClearEvents() => LastEvent = new HashSet<ITestEvent>();

            public UpstreamProbe<T> NewUpstreamProbe<T>(string name) => new UpstreamProbe<T>(this, name);

            public DownstreamProbe<T> NewDownstreamProbe<T>(string name) => new DownstreamProbe<T>(this, name);

            public class UpstreamProbe<T> : GraphInterpreter.UpstreamBoundaryStageLogic
            {
                private readonly string _name;

                public UpstreamProbe(TestSetup setup, string name)
                {
                    _name = name;
                    Outlet = new Outlet<T>("out") {Id = 0};

                    var probe = this;
                    SetHandler(Outlet, () => setup.LastEvent.Add(new RequestOne(probe)), () => setup.LastEvent.Add(new Cancel(probe)));
                }

                public sealed override Outlet Out => Outlet;

                public Outlet<T> Outlet { get; }

#pragma warning disable CS0162 // Disabled since the flag can be set while debugging
                public void OnNext(T element, int eventLimit = int.MaxValue)
                {
                    if (GraphInterpreter.IsDebug)
                        Console.WriteLine($"----- NEXT: {this} {element}");
                    Push(Outlet, element);
                    Interpreter.Execute(eventLimit);
                }

                public void OnComplete(int eventLimit = int.MaxValue)
                {
                    if (GraphInterpreter.IsDebug)
                        Console.WriteLine($"----- COMPLETE: {this}");
                    Complete(Outlet);
                    Interpreter.Execute(eventLimit);
                }

                public void OnFailure(int eventLimit = int.MaxValue, Exception ex = null)
                {
                    if (GraphInterpreter.IsDebug)
                        Console.WriteLine($"----- FAIL: {this}");
                    Fail(Outlet, ex);
                    Interpreter.Execute(eventLimit);
                }
#pragma warning restore CS0162

                public override string ToString() => _name;
            }

            public class DownstreamProbe<T> : GraphInterpreter.DownstreamBoundaryStageLogic
            {
                private readonly string _name;

                public DownstreamProbe(TestSetup setup, string name)
                {
                    _name = name;
                    Inlet = new Inlet<T>("in") {Id = 0};

                    var probe = this;
                    SetHandler(Inlet, () => setup.LastEvent.Add(new OnNext(probe, Grab(Inlet))),
                        () => setup.LastEvent.Add(new OnComplete(probe)),
                        ex => setup.LastEvent.Add(new OnError(probe, ex)));
                }

                public sealed override Inlet In => Inlet;

                public Inlet<T> Inlet { get; }

#pragma warning disable CS0162 // Disabled since the flag can be set while debugging
                public void RequestOne(int eventLimit = int.MaxValue)
                {
                    if (GraphInterpreter.IsDebug)
                        Console.WriteLine($"----- REQ: {this}");
                    Pull(Inlet);
                    Interpreter.Execute(eventLimit);
                }

                public void Cancel(int eventLimit = int.MaxValue)
                {
                    if (GraphInterpreter.IsDebug)
                        Console.WriteLine($"----- CANCEL: {this}");
                    Cancel(Inlet);
                    Interpreter.Execute(eventLimit);
                }
#pragma warning restore CS0162

                public override string ToString() => _name;
            }
        }

        public class PortTestSetup : TestSetup
        {
            private readonly bool _chasing;
            public UpstreamPortProbe<int> Out { get; }
            public DownstreamPortProbe<int> In { get; }

            public PortTestSetup(ActorSystem system, bool chasing = false) : base(system)
            {
                _chasing = chasing;
                var propagateStage = new EventPropagateStage();

                var assembly = !chasing
                    ? new GraphAssembly(new IGraphStageWithMaterializedValue<Shape, object>[0], new Attributes[0],
                        new Inlet[] {null}, new[] {-1}, new Outlet[] {null}, new[] {-1})
                    : new GraphAssembly(new[] {propagateStage}, new[] {Attributes.None},
                        new Inlet[] {propagateStage.In, null}, new[] {0, -1}, new Outlet[] {null, propagateStage.Out},
                        new[] {-1, 0});

                Out = new UpstreamPortProbe<int>(this);
                In = new DownstreamPortProbe<int>(this);

                ManualInit(assembly);
                Interpreter.AttachDownstreamBoundary(Interpreter.Connections[chasing ? 1 : 0], In);
                Interpreter.AttachUpstreamBoundary(Interpreter.Connections[0], Out);
                Interpreter.Init(null);
            }

            public class EventPropagateStage : GraphStage<FlowShape<int, int>>
            {
                private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
                {
                    private readonly EventPropagateStage _stage;

                    public Logic(EventPropagateStage stage) :base(stage.Shape)
                    {
                        _stage = stage;

                        SetHandler(stage.In, this);
                        SetHandler(stage.Out, this);
                    }

                    public void OnPush() => Push(_stage.Out, Grab(_stage.In));

                    public void OnUpstreamFinish() => Complete(_stage.Out);

                    public void OnUpstreamFailure(Exception e) => Fail(_stage.Out, e);

                    public void OnPull() => Pull(_stage.In);

                    public void OnDownstreamFinish() => Cancel(_stage.In);
                }

                public EventPropagateStage() => Shape  = new FlowShape<int, int>(In, Out);

                public Inlet<int> In { get; } = new Inlet<int>("Propagate.in");

                public Outlet<int> Out { get; } = new Outlet<int>("Propagate.out");

                public override FlowShape<int, int> Shape { get; }

                protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
            }

            // Step() means different depending whether we have a stage between the two probes or not
            public override void Step() => Interpreter.Execute(!_chasing ? 1 : 2);

            public class UpstreamPortProbe<T> : UpstreamProbe<T>
            {
                public UpstreamPortProbe(TestSetup setup) : base(setup, "upstreamPort")
                {
                }

                public bool IsAvailable() => IsAvailable(Outlet);

                public bool IsClosed() => IsClosed(Outlet);

                public void Push(T element) => Push(Outlet, element);

                public void Complete() => Complete(Outlet);

                public void Fail(Exception ex) => Fail(Outlet, ex);
            }

            public class DownstreamPortProbe<T> : DownstreamProbe<T>
            {
                public DownstreamPortProbe(TestSetup setup) : base(setup, "downstreamPort")
                {
                    var probe = this;
                    SetHandler(Inlet, () =>
                        {
                            // Modified onPush that does not Grab() automatically the element. This access some internals.
                            var internalEvent = PortToConn[In.Id].Slot;

                            if (internalEvent is GraphInterpreter.Failed failed)
                                ((PortTestSetup)setup).LastEvent.Add(new OnNext(probe,
                                    failed.PreviousElement));
                            else
                                ((PortTestSetup)setup).LastEvent.Add(new OnNext(probe, internalEvent));
                        },
                        () => ((PortTestSetup)setup).LastEvent.Add(new OnComplete(probe)),
                        ex => ((PortTestSetup)setup).LastEvent.Add(new OnError(probe, ex))
                    );
                }

                public bool IsAvailable() => IsAvailable(Inlet);

                public bool HasBeenPulled() => HasBeenPulled(Inlet);

                public bool IsClosed() => IsClosed(Inlet);

                public void Pull() => Pull(Inlet);

                public void Cancel() => Cancel(Inlet);

                public T Grab() => Grab(Inlet);
            }
        }

        public class FailingStageSetup : TestSetup
        {
            public new UpstreamPortProbe<int> Upstream { get; }
            public new DownstreamPortProbe<int> Downstream { get; }

            private bool _failOnNextEvent;
            private bool _failOnPostStop;
            private readonly Inlet<int> _stageIn;
            private readonly Outlet<int> _stageOut;
            private readonly FlowShape<int, int> _stageShape;

            // Must be lazy because I turned this stage "inside-out" therefore changing initialization order
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

                GraphStage<FlowShape<int, int>> sandwitchStage = new SandwitchStage(this);

                Builder(sandwitchStage)
                    .Connect(Upstream, _stageIn)
                    .Connect(_stageOut, Downstream)
                    .Init();
            }

            public void FailOnNextEvent() => _failOnNextEvent = true;

            public void FailOnPostStop() => _failOnPostStop = true;

            public Exception TestException() => new TestException("test");

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

                public override void PreStart() => MayFail(() => _setup.LastEvent.Add(new PreStart(this)));

                public override void PostStop()
                {
                    if (!_setup._failOnPostStop)
                        _setup.LastEvent.Add(new PostStop(this));
                    else throw _setup.TestException();
                }

                public override string ToString() => "stage";
            }

            public class SandwitchStage : GraphStage<FlowShape<int, int>>
            {
                private readonly FailingStageSetup _setup;

                public SandwitchStage(FailingStageSetup setup) => _setup = setup;

                public override FlowShape<int, int> Shape => _setup._stageShape;

                protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => _setup.Stage.Value;

                public override string ToString() => "stage";
            }

            public class UpstreamPortProbe<T> : UpstreamProbe<T>
            {
                public UpstreamPortProbe(TestSetup setup) : base(setup, "upstreamPort")
                {
                }

                public void Push(T element) => Push(Outlet, element);

                public void Complete() => Complete(Outlet);

                public void Fail(Exception ex) => Fail(Outlet, ex);
            }

            public class DownstreamPortProbe<T> : DownstreamProbe<T>
            {
                public DownstreamPortProbe(TestSetup setup) : base(setup, "downstreamPort")
                {
                }

                public void Pull() => Pull(Inlet);

                public void Cancel() => Cancel(Inlet);
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
                protected bool Equals(OnComplete other) => true;

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != typeof (OnComplete)) return false;
                    return Equals((OnComplete) obj);
                }

                public override int GetHashCode() => 0;
            }

            public class Cancel : ITestEvent
            {
                protected bool Equals(Cancel other) => true;

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != typeof (Cancel)) return false;
                    return Equals((Cancel) obj);
                }

                public override int GetHashCode() => 0;
            }

            public class OnError : ITestEvent
            {
                public Exception Cause { get; }

                public OnError(Exception cause) => Cause = cause;

                protected bool Equals(OnError other) => Equals(Cause, other.Cause);

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((OnError) obj);
                }

                public override int GetHashCode() => Cause?.GetHashCode() ?? 0;
            }

            public class OnNext : ITestEvent
            {
                public object Element { get; }

                public OnNext(object element) => Element = element;

                protected bool Equals(OnNext other)
                {
                    return Element is IEnumerable enumerable
                        ? enumerable.Cast<object>().SequenceEqual(((IEnumerable) other.Element).Cast<object>())
                        : Equals(Element, other.Element);
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != GetType()) return false;
                    return Equals((OnNext) obj);
                }

                public override int GetHashCode() => Element?.GetHashCode() ?? 0;
            }

            public class RequestOne : ITestEvent
            {
                protected bool Equals(RequestOne other) => true;

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != typeof (RequestOne)) return false;
                    return Equals((RequestOne) obj);
                }

                public override int GetHashCode() => 0;
            }

            public class RequestAnother : ITestEvent
            {
                protected bool Equals(RequestAnother other) => true;

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if (obj.GetType() != typeof (RequestAnother)) return false;
                    return Equals((RequestAnother) obj);
                }

                public override int GetHashCode() => 0;
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
                private readonly Outlet<T> _outlet;

                public UpstreamOneBoundedProbe(OneBoundedSetup setup)
                {
                    _setup = setup;
                    _outlet = new Outlet<T>("out") {Id = 0};

                    SetHandler(_outlet, () =>
                    {
                        if (setup.LastEvent.OfType<RequestOne>().Any())
                            setup.LastEvent.Add(new RequestAnother());
                        else
                            setup.LastEvent.Add(new RequestOne());
                    }, () => setup.LastEvent.Add(new Cancel()));
                }

                public override Outlet Out => _outlet;

                public void OnNext(T element)
                {
                    Push(_outlet, element);
                    _setup.Run();
                }

                public void OnComplete()
                {
                    Complete(_outlet);
                    _setup.Run();
                }

                public void OnNextAndComplete(T element)
                {
                    Push(_outlet, element);
                    Complete(_outlet);
                    _setup.Run();
                }

                public void OnError(Exception ex)
                {
                    Fail(_outlet, ex);
                    _setup.Run();
                }
            }

            public class DownstreamOneBoundedPortProbe<T> : GraphInterpreter.DownstreamBoundaryStageLogic
            {
                private readonly OneBoundedSetup _setup;
                private readonly Inlet<object> _inlet;

                public DownstreamOneBoundedPortProbe(OneBoundedSetup setup)
                {
                    _setup = setup;
                    _inlet = new Inlet<object>("in") {Id = 0};

                    SetHandler(_inlet, () => setup.LastEvent.Add(new OnNext(Grab(_inlet))),
                    () => setup.LastEvent.Add(new OnComplete()),
                    ex => setup.LastEvent.Add(new OnError(ex)));
                }

                public override Inlet In => _inlet;

                public void RequestOne()
                {
                    Pull(_inlet);
                    _setup.Run();
                }

                public void Cancel()
                {
                    Cancel(_inlet);
                    _setup.Run();
                }
            }
        }

        public class OneBoundedSetup<TIn, TOut> : OneBoundedSetup
        {
            public OneBoundedSetup(ActorSystem system, params IGraphStageWithMaterializedValue<Shape, object>[] ops) : base(system)
            {
                Ops = ops;
                Upstream = new UpstreamOneBoundedProbe<TIn>(this);
                Downstream = new DownstreamOneBoundedPortProbe<TOut>(this);

                Initialize();
                Run(); // Detached stages need the prefetch
            }

            public IGraphStageWithMaterializedValue<Shape, object>[] Ops { get; }
            public new UpstreamOneBoundedProbe<TIn> Upstream { get; }
            public new DownstreamOneBoundedPortProbe<TOut> Downstream { get; }

            protected sealed override void Run() => Interpreter.Execute(int.MaxValue);

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
            public OneBoundedSetup(ActorSystem system, params IGraphStageWithMaterializedValue<Shape, object>[] ops) : base(system, ops)
            {
            }
        }

        public PushPullGraphStage<TIn, TOut> ToGraphStage<TIn, TOut>(IStage<TIn, TOut> stage)
        {
            var s = stage;
            return new PushPullGraphStage<TIn, TOut>(_ => s, Attributes.None);
        }

        public IGraphStageWithMaterializedValue<Shape, object>[] ToGraphStage<TIn, TOut>(IStage<TIn, TOut>[] stages)
        {
            return stages.Select(ToGraphStage).Cast<IGraphStageWithMaterializedValue<Shape, object>>().ToArray();
        }

        public void WithTestSetup(Action<TestSetup, Func<ISet<TestSetup.ITestEvent>>> spec)
        {
            var setup = new TestSetup(Sys);
            spec(setup, setup.LastEvents);
        }

        public void WithTestSetup(
            Action
                <TestSetup, Func<IGraphStageWithMaterializedValue<Shape, object>, BaseBuilder.AssemblyBuilder>,
                    Func<ISet<TestSetup.ITestEvent>>> spec)
        {
            var setup = new TestSetup(Sys);
            spec(setup, g => setup.Builder(g), setup.LastEvents);
        }

        public void WithTestSetup(
            Action
                <TestSetup, Func<IGraphStageWithMaterializedValue<Shape, object>[], BaseBuilder.AssemblyBuilder>,
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

        public void WithOneBoundedSetup<T>(IGraphStageWithMaterializedValue<Shape, object> op,
            Action
                <Func<ISet<OneBoundedSetup.ITestEvent>>, OneBoundedSetup.UpstreamOneBoundedProbe<T>,
                    OneBoundedSetup.DownstreamOneBoundedPortProbe<T>>
                spec)
        {
            WithOneBoundedSetup<T>(new[] {op}, spec);
            
        }

        public void WithOneBoundedSetup<T>(IGraphStageWithMaterializedValue<Shape, object>[] ops,
            Action
                <Func<ISet<OneBoundedSetup.ITestEvent>>, OneBoundedSetup.UpstreamOneBoundedProbe<T>, OneBoundedSetup.DownstreamOneBoundedPortProbe<T>>
                spec)
        {
            var setup = new OneBoundedSetup<T>(Sys, ops);
            spec(setup.LastEvents, setup.Upstream, setup.Downstream);
        }

        public void WithOneBoundedSetup<TIn, TOut>(IGraphStageWithMaterializedValue<Shape, object> op,
            Action
                <Func<ISet<OneBoundedSetup.ITestEvent>>, OneBoundedSetup.UpstreamOneBoundedProbe<TIn>,
                    OneBoundedSetup.DownstreamOneBoundedPortProbe<TOut>>
                spec)
        {
            WithOneBoundedSetup(new[] {op}, spec);
        }

        public void WithOneBoundedSetup<TIn, TOut>(IGraphStageWithMaterializedValue<Shape, object>[] ops,
            Action
                <Func<ISet<OneBoundedSetup.ITestEvent>>, OneBoundedSetup.UpstreamOneBoundedProbe<TIn>, OneBoundedSetup.DownstreamOneBoundedPortProbe<TOut>>
                spec)
        {
            var setup = new OneBoundedSetup<TIn, TOut>(Sys, ops);
            spec(setup.LastEvents, setup.Upstream, setup.Downstream);
        }

        public void WithOneBoundedSetup<TIn, TOut>(IGraphStageWithMaterializedValue<FlowShape<TIn, TOut>, object> op,
            Action
                <Func<ISet<OneBoundedSetup.ITestEvent>>, OneBoundedSetup.UpstreamOneBoundedProbe<TIn>, OneBoundedSetup.DownstreamOneBoundedPortProbe<TOut>>
                spec)
        {
            WithOneBoundedSetup(new[] { op }, spec);
        }

        public void WithOneBoundedSetup<TIn, TOut>(IGraphStageWithMaterializedValue<FlowShape<TIn, TOut>, object>[] ops,
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
