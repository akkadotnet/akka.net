//-----------------------------------------------------------------------
// <copyright file="GraphStages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Streams.Implementation.Fusing
{
    /// <summary>
    /// Factory methods for internal graph stages used by stream construction.
    /// </summary>
    public static class GraphStages
    {
        /// <summary>
        /// Returns the pass-through stage used to connect a flow without changing its elements.
        /// </summary>
        /// <typeparam name="T">The type of elements flowing through the stage.</typeparam>
        /// <returns>The singleton identity stage for elements of type <typeparamref name="T"/></returns>
        public static SimpleLinearGraphStage<T> Identity<T>() => Implementation.Fusing.Identity<T>.Instance;

        /// <summary>
        /// Creates the internal pass-through stage that exposes stream completion as a task.
        /// </summary>
        /// <typeparam name="T">The type of elements flowing through the stage.</typeparam>
        /// <returns>The singleton termination-watcher stage for elements of type <typeparamref name="T"/></returns>
        internal static GraphStageWithMaterializedValue<FlowShape<T, T>, Task<Done>> TerminationWatcher<T>()
            => Implementation.Fusing.TerminationWatcher<T>.Instance;

        /// <summary>
        /// Fusing graphs that have cycles involving FanIn stages might lead to deadlocks if
        /// demand is not carefully managed.
        /// 
        /// This means that FanIn stages need to early pull every relevant input on startup.
        /// This can either be implemented inside the stage itself, or this method can be used,
        /// which adds a detacher stage to every input.
        /// </summary>
        /// <typeparam name="T">The type of elements accepted by the fan-in stage.</typeparam>
        /// <param name="stage">The fan-in stage whose inputs are each connected through a detacher.</param>
        /// <returns>A graph containing the supplied fan-in stage with a detacher on each input</returns>
        internal static IGraph<UniformFanInShape<T, T>, NotUsed> WithDetachedInputs<T>(GraphStage<UniformFanInShape<T, T>> stage)
        {
            return GraphDsl.Create(builder =>
            {
                var concat = builder.Add(stage);
                var detachers = concat.Ins.Select(inlet =>
                {
                    var detacher = builder.Add(new Detacher<T>());
                    builder.From(detacher).To(inlet);
                    return detacher.Inlet;
                }).ToArray();
                return new UniformFanInShape<T, T>(concat.Out, detachers);
            });
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public class GraphStageModule : AtomicModule
    {
        /// <summary>
        /// The graph stage represented by this module.
        /// </summary>
        public readonly IGraphStageWithMaterializedValue<Shape, object> Stage;

        /// <summary>
        /// Creates a module for a graph stage, its shape, and its attributes.
        /// </summary>
        /// <param name="shape">The exposed shape of the stage.</param>
        /// <param name="attributes">The attributes applied to the stage.</param>
        /// <param name="stage">The graph stage implementation.</param>
        public GraphStageModule(Shape shape, Attributes attributes, IGraphStageWithMaterializedValue<Shape, object> stage)
        {
            Shape = shape;
            Attributes = attributes;
            Stage = stage;
        }

        /// <summary>
        /// The exposed shape of the stage.
        /// </summary>
        public override Shape Shape { get; }

        /// <summary>
        /// Creates a copied module that uses the supplied shape.
        /// </summary>
        /// <param name="shape">The replacement shape.</param>
        /// <returns>A copied module using the supplied shape and this module as its source.</returns>
        public override IModule ReplaceShape(Shape shape) => new CopiedModule(shape, Attributes.None, this);

        /// <summary>
        /// Creates a copied module with a deep copy of this stage’s shape.
        /// </summary>
        /// <returns>A copied module with a deep-copied shape.</returns>
        public override IModule CarbonCopy() => ReplaceShape(Shape.DeepCopy());

        /// <summary>
        /// The attributes applied to the stage.
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// Creates a module for this stage with different attributes.
        /// </summary>
        /// <param name="attributes">The attributes to apply.</param>
        /// <returns>A graph-stage module with the supplied attributes.</returns>
        public override IModule WithAttributes(Attributes attributes) => new GraphStageModule(Shape, attributes, Stage);

        /// <summary>
        /// Returns a diagnostic representation of this graph-stage module.
        /// </summary>
        /// <returns>A string containing the stage and module hash code.</returns>
        public override string ToString() => $"GraphStage({Stage}) [{GetHashCode()}%08x]";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">The type of elements flowing through the stage.</typeparam>
    [InternalApi]
    public abstract class SimpleLinearGraphStage<T> : GraphStage<FlowShape<T, T>>
    {
        /// <summary>
        /// The stage inlet.
        /// </summary>
        public readonly Inlet<T> Inlet;
        /// <summary>
        /// The stage outlet.
        /// </summary>
        public readonly Outlet<T> Outlet;

        /// <summary>
        /// Creates an inlet and outlet whose names use the supplied prefix.
        /// </summary>
        /// <param name="name">The prefix for port names, or null to use the stage type name.</param>
        protected SimpleLinearGraphStage(string name = null)
        {
            name = name ?? GetType().Name;
            Inlet = new Inlet<T>(name + ".in");
            Outlet = new Outlet<T>(name + ".out");
            Shape = new FlowShape<T, T>(Inlet, Outlet);
        }

        /// <summary>
        /// The flow shape containing this stage’s inlet and outlet.
        /// </summary>
        public override FlowShape<T, T> Shape { get; }
    }

    /// <summary>
    /// Passes each element from its inlet to its outlet unchanged.
    /// </summary>
    /// <typeparam name="T">The type of elements flowing through the stage.</typeparam>
    public sealed class Identity<T> : SimpleLinearGraphStage<T>
    {
        #region internal classes
        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Identity<T> _stage;

            public Logic(Identity<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandlers(stage.Inlet, stage.Outlet, this);
            }

            public override void OnPush() => Push(_stage.Outlet, Grab(_stage.Inlet));

            public override void OnPull() => Pull(_stage.Inlet);
        }
        #endregion

        /// <summary>
        /// The shared identity-stage instance.
        /// </summary>
        public static readonly Identity<T> Instance = new();

        private Identity()
        {
        }

        /// <summary>
        /// The default name attribute for this stage.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("identityOp");

        /// <summary>
        /// Creates the logic that forwards each element unchanged.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes inherited by this stage.</param>
        /// <returns>The stage logic that forwards input elements unchanged to the outlet.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">The type of elements flowing through the stage.</typeparam>
    [InternalApi]
    public sealed class Detacher<T> : SimpleLinearGraphStage<T>
    {
        #region internal classes
        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Detacher<T> _stage;

            public Logic(Detacher<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandlers(stage.Inlet, stage.Outlet, this);
            }

            public override void PreStart() => TryPull(_stage.Inlet);

            public override void OnPush()
            {
                var outlet = _stage.Outlet;
                if (IsAvailable(outlet))
                {
                    var inlet = _stage.Inlet;
                    Push(outlet, Grab(inlet));
                    TryPull(inlet);
                }
            }

            public override void OnUpstreamFinish()
            {
                if (!IsAvailable(_stage.Inlet))
                    CompleteStage();
            }

            public override void OnPull()
            {
                var inlet = _stage.Inlet;
                if (IsAvailable(inlet))
                {
                    var outlet = _stage.Outlet;
                    Push(outlet, Grab(inlet));
                    if (IsClosed(inlet))
                        CompleteStage();
                    else
                        Pull(inlet);
                }
            }
        }
        #endregion

        /// <summary>
        /// Creates a stage that pulls one element ahead and buffers it until downstream demand is available.
        /// </summary>
        public Detacher() : base("Detacher")
        {
        }

        /// <summary>
        /// The default name attribute for this stage.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("Detacher");

        /// <summary>
        /// Creates the logic that buffers an element between upstream and downstream demand.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes inherited by this stage.</param>
        /// <returns>The detacher stage logic.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns the stage name.
        /// </summary>
        /// <returns>The string "Detacher"</returns>
        public override string ToString() => "Detacher";
    }

    /// <summary>
    /// Passes elements through and materializes a task that completes when the stream terminates.
    /// </summary>
    /// <typeparam name="T">The type of elements flowing through the stage.</typeparam>
    internal sealed class TerminationWatcher<T> : GraphStageWithMaterializedValue<FlowShape<T, T>, Task<Done>>
    {
        /// <summary>
        /// The shared termination-watcher stage instance.
        /// </summary>
        public static readonly TerminationWatcher<T> Instance = new();

        #region internal classes 

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly TerminationWatcher<T> _stage;
            private readonly TaskCompletionSource<Done> _finishPromise;
            private bool _completedSignalled;

            public Logic(TerminationWatcher<T> stage, TaskCompletionSource<Done> finishPromise) : base(stage.Shape)
            {
                _stage = stage;
                _finishPromise = finishPromise;

                SetHandler(stage._inlet, this);
                SetHandler(stage._outlet, this);
            }

            public override void OnPush() => Push(_stage._outlet, Grab(_stage._inlet));

            public override void OnUpstreamFinish()
            {
                _finishPromise.TrySetResult(Done.Instance);
                _completedSignalled = true;
                CompleteStage();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                _finishPromise.TrySetException(e);
                _completedSignalled = true;
                FailStage(e);
            }

            public override void OnPull() => Pull(_stage._inlet);

            public override void OnDownstreamFinish(Exception cause)
            {
                if (cause is SubscriptionWithCancelException.NonFailureCancellation)
                    _finishPromise.TrySetResult(Done.Instance);
                else
                    _finishPromise.TrySetException(cause);
                
                _completedSignalled = true;
                CancelStage(cause);
            }

            public override void PostStop()
            {
                if (!_completedSignalled)
                    _finishPromise.TrySetException(new AbruptStageTerminationException(this));
            }
        }

        #endregion

        private readonly Inlet<T> _inlet = new("TerminationWatcher.in");
        private readonly Outlet<T> _outlet = new("TerminationWatcher.out");

        private TerminationWatcher()
        {
            Shape = new FlowShape<T, T>(_inlet, _outlet);
        }

        /// <summary>
        /// The default attributes for this stage.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.TerminationWatcher;

        /// <summary>
        /// The flow shape containing this stage’s inlet and outlet.
        /// </summary>
        public override FlowShape<T, T> Shape { get; }

        /// <summary>
        /// Creates the pass-through logic and its stream-termination task.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes inherited by this stage.</param>
        /// <returns>The stage logic and a task that completes on upstream completion or non-failure downstream cancellation, and faults on failure or abrupt termination</returns>
        public override ILogicAndMaterializedValue<Task<Done>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var finishPromise = TaskEx.NonBlockingTaskCompletionSource<Done>();
            return new LogicAndMaterializedValue<Task<Done>>(new Logic(this, finishPromise), finishPromise.Task);
        }

        /// <summary>
        /// Returns the stage name.
        /// </summary>
        /// <returns>The string "TerminationWatcher"</returns>
        public override string ToString() => "TerminationWatcher";
    }

    // TODO: fix typo
    /// <summary>
    /// Stores the latest stream state or received element for a flow monitor.
    /// </summary>
    /// <typeparam name="T">The type of elements observed by the flow monitor.</typeparam>
    internal sealed class FLowMonitorImpl<T> : AtomicReference<object>, IFlowMonitor
    {
        /// <summary>
        /// Creates a monitor initialized to the not-yet-subscribed state.
        /// </summary>
        public FLowMonitorImpl() : base(FlowMonitor.Initialized.Instance)
        {

        }

        /// <summary>
        /// Gets the latest stream state, wrapping values assignable to <typeparamref name="T"/> as received elements; broad element types such as <c>object</c> can also match state markers.
        /// </summary>
        public FlowMonitor.IStreamState State
        {
            get
            {
                var value = Value;
                if (value is T value1)
                    return new FlowMonitor.Received<T>(value1);

                return value as FlowMonitor.IStreamState;
            }
        }
    }

    /// <summary>
    /// Passes elements through while materializing a monitor of stream state.
    /// </summary>
    /// <typeparam name="T">The type of elements flowing through the stage.</typeparam>
    internal sealed class MonitorFlow<T> : GraphStageWithMaterializedValue<FlowShape<T, T>, IFlowMonitor>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly MonitorFlow<T> _stage;
            private readonly FLowMonitorImpl<T> _monitor;

            public Logic(MonitorFlow<T> stage, FLowMonitorImpl<T> monitor) : base(stage.Shape)
            {
                _stage = stage;
                _monitor = monitor;

                SetHandlers(stage.In, stage.Out, this);
            }

            public override void OnPush()
            {
                var message = Grab(_stage.In);
                Push(_stage.Out, message);
                _monitor.Value = message is FlowMonitor.IStreamState
                    ? new FlowMonitor.Received<T>(message)
                    : (object)message;
            }

            public override void OnUpstreamFinish()
            {
                CompleteStage();
                _monitor.Value = FlowMonitor.Finished.Instance;
            }

            public override void OnUpstreamFailure(Exception e)
            {
                FailStage(e);
                _monitor.Value = new FlowMonitor.Failed(e);
            }

            public override void OnPull() => Pull(_stage.In);

            public override void OnDownstreamFinish(Exception cause)
            {
                InternalOnDownstreamFinish(cause);
                _monitor.Value = FlowMonitor.Finished.Instance;
            }

            public override void PostStop()
            {
                if (!(_monitor.State is FlowMonitor.Finished) && !(_monitor.State is FlowMonitor.Failed))
                    _monitor.Value = new FlowMonitor.Failed(new AbruptStageTerminationException(this));
            }

            public override string ToString() => "MonitorFlowLogic";
        }

        #endregion

        /// <summary>
        /// Creates a flow monitor with one inlet and one outlet.
        /// </summary>
        public MonitorFlow()
        {
            Shape = new FlowShape<T, T>(In, Out);
        }

        /// <summary>
        /// The stage inlet.
        /// </summary>
        public Inlet<T> In { get; } = new("MonitorFlow.in");

        /// <summary>
        /// The stage outlet.
        /// </summary>
        public Outlet<T> Out { get; } = new("MonitorFlow.out");

        /// <summary>
        /// The flow shape containing this stage’s inlet and outlet.
        /// </summary>
        public override FlowShape<T, T> Shape { get; }

        /// <summary>
        /// Creates the pass-through logic and a monitor for this stream.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes inherited by this stage.</param>
        /// <returns>The stage logic and its flow monitor.</returns>
        public override ILogicAndMaterializedValue<IFlowMonitor> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var monitor = new FLowMonitorImpl<T>();
            var logic = new Logic(this, monitor);
            return new LogicAndMaterializedValue<IFlowMonitor>(logic, monitor);
        }

        /// <summary>
        /// Returns the stage name.
        /// </summary>
        /// <returns>The string "MonitorFlow"</returns>
        public override string ToString() => "MonitorFlow";
    }

    /// <summary>
    /// Emits the configured element on timer ticks when downstream demand is available and materializes a cancellation handle.
    /// </summary>
    /// <typeparam name="T">The type of the element emitted on each tick.</typeparam>
    public sealed class TickSource<T> : GraphStageWithMaterializedValue<SourceShape<T>, ICancelable>
    {
        #region internal classes

        [SuppressMessage("ReSharper", "MethodSupportsCancellation")]
        private sealed class Logic : TimerGraphStageLogic, ICancelable
        {
            private readonly TickSource<T> _stage;
            private readonly AtomicBoolean _cancelled = new();

            private readonly AtomicReference<Action<NotUsed>> _cancelCallback = new(null);

            public Logic(TickSource<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(_stage.Out, EagerTerminateOutput);
            }

            public override void PreStart()
            {
                _cancelCallback.Value = GetAsyncCallback<NotUsed>(_ => CompleteStage());

                if (_cancelled.Value)
                    CompleteStage();
                else
                    ScheduleRepeatedly("TickTimer", _stage._initialDelay, _stage._interval);
            }

            protected internal override void OnTimer(object timerKey)
            {
                if (_cancelled.Value)
                    return;

                if (IsAvailable(_stage.Out))
                    Push(_stage.Out, _stage._tick);
            }

            public void Cancel()
            {
                if (!_cancelled.GetAndSet(true))
                    _cancelCallback.Value?.Invoke(NotUsed.Instance);
            }

            public bool IsCancellationRequested => _cancelled.Value;

            public CancellationToken Token { get; }

            public void CancelAfter(TimeSpan delay) => Task.Delay(delay).ContinueWith(_ => Cancel());

            public void CancelAfter(int millisecondsDelay) => Task.Delay(millisecondsDelay).ContinueWith(_ => Cancel());

            public void Cancel(bool throwOnFirstException) => Cancel();

            public override string ToString() => "TickSourceLogic";
        }

        #endregion

        private readonly TimeSpan _initialDelay;
        private readonly TimeSpan _interval;
        private readonly T _tick;

        /// <summary>
        /// Creates a source that emits the supplied element after the initial delay and on later timer ticks when downstream demand is available.
        /// </summary>
        /// <param name="initialDelay">The delay before the first tick.</param>
        /// <param name="interval">The delay between subsequent ticks.</param>
        /// <param name="tick">The element emitted for each tick.</param>
        public TickSource(TimeSpan initialDelay, TimeSpan interval, T tick)
        {
            _initialDelay = initialDelay;
            _interval = interval;
            _tick = tick;
            Shape = new SourceShape<T>(Out);
        }

        /// <summary>
        /// The default attributes for this source.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.TickSource;

        /// <summary>
        /// The source outlet.
        /// </summary>
        public Outlet<T> Out { get; } = new("TimerSource.out");

        /// <summary>
        /// The source shape containing the outlet.
        /// </summary>
        public override SourceShape<T> Shape { get; }

        /// <summary>
        /// Creates the timer logic and its cancellation handle.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes inherited by this source.</param>
        /// <returns>The timer stage logic and its cancelable handle.</returns>
        public override ILogicAndMaterializedValue<ICancelable> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this);
            return new LogicAndMaterializedValue<ICancelable>(logic, logic);
        }

        /// <summary>
        /// Returns a diagnostic representation of this timer source.
        /// </summary>
        /// <returns>A string containing the initial delay, interval, and emitted element.</returns>
        public override string ToString() => $"TickSource({_initialDelay}, {_interval}, {_tick})";
    }

    /// <summary>
    /// Represents a source stage that emits a materialized value.
    /// </summary>
    public interface IMaterializedValueSource
    {
        /// <summary>
        /// The graph module associated with this materialized-value source.
        /// </summary>
        IModule Module { get; }
        /// <summary>
        /// Creates a copy of this materialized-value source.
        /// </summary>
        /// <returns>A source instance that emits the associated materialized value.</returns>
        IMaterializedValueSource CopySource();
        /// <summary>
        /// The outlet through which the materialized value is emitted.
        /// </summary>
        Outlet Outlet { get; }
        /// <summary>
        /// The materialized-value computation represented by this source.
        /// </summary>
        StreamLayout.IMaterializedValueNode Computation { get; }
        /// <summary>
        /// Sets the value that the source will emit.
        /// </summary>
        /// <param name="result">The materialized value to emit.</param>
        void SetValue(object result);
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// This source is not reusable, it is only created internally.
    /// </summary>
    /// <typeparam name="T">The type of the materialized value emitted by the source.</typeparam>
    [InternalApi]
    public sealed class MaterializedValueSource<T> : GraphStage<SourceShape<T>>, IMaterializedValueSource
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            private readonly MaterializedValueSource<T> _source;

            public Logic(MaterializedValueSource<T> source) : base(source.Shape)
            {
                _source = source;
                SetHandler(source.Outlet, EagerTerminateOutput);
            }

            public override void PreStart()
            {
                var cb = GetAsyncCallback<T>(element => Emit(_source.Outlet, element, CompleteStage));
                _source._promise.Task.ContinueWith(task => cb(task.Result), TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        #endregion

        private static readonly Attributes Name = Attributes.CreateName("matValueSource");

        /// <summary>
        /// The materialized-value computation represented by this source.
        /// </summary>
        public StreamLayout.IMaterializedValueNode Computation { get; }

        Outlet IMaterializedValueSource.Outlet => Outlet;

        /// <summary>
        /// The outlet through which the materialized value is emitted.
        /// </summary>
        public readonly Outlet<T> Outlet;

        private readonly TaskCompletionSource<T> _promise = TaskEx.NonBlockingTaskCompletionSource<T>();

        /// <summary>
        /// Creates a source for the supplied materialized-value computation and outlet.
        /// </summary>
        /// <param name="computation">The computation whose result this source emits.</param>
        /// <param name="outlet">The outlet used by the source shape.</param>
        public MaterializedValueSource(StreamLayout.IMaterializedValueNode computation, Outlet<T> outlet)
        {
            Computation = computation;
            Outlet = outlet;
            Shape = new SourceShape<T>(Outlet);
        }

        /// <summary>
        /// Creates a source with a default materialized-value outlet.
        /// </summary>
        /// <param name="computation">The computation whose result this source emits.</param>
        public MaterializedValueSource(StreamLayout.IMaterializedValueNode computation) : this(computation, new Outlet<T>("matValue")) { }

        /// <summary>
        /// The default name attribute for this source.
        /// </summary>
        protected override Attributes InitialAttributes => Name;

        /// <summary>
        /// The source shape containing the materialized-value outlet.
        /// </summary>
        public override SourceShape<T> Shape { get; }

        /// <summary>
        /// Completes the source’s value promise with the materialized value.
        /// </summary>
        /// <param name="value">The value to emit from this source.</param>
        public void SetValue(T value) => _promise.SetResult(value);

        void IMaterializedValueSource.SetValue(object result) => SetValue((T)result);

        /// <summary>
        /// Creates another source using this computation and outlet.
        /// </summary>
        /// <returns>A new source instance with a new value promise.</returns>
        public MaterializedValueSource<T> CopySource() => new(Computation, Outlet);

        IMaterializedValueSource IMaterializedValueSource.CopySource() => CopySource();

        /// <summary>
        /// Creates the logic that emits the completed materialized value.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes inherited by this source.</param>
        /// <returns>The stage logic that waits for and emits the value.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns a diagnostic representation of this source and its computation.
        /// </summary>
        /// <returns>A string containing the materialized-value computation.</returns>
        public override string ToString() => $"MaterializedValueSource({Computation})";
    }

    /// <summary>
    /// Emits one configured element when downstream requests it, then completes.
    /// </summary>
    /// <typeparam name="T">The type of the emitted element.</typeparam>
    public sealed class SingleSource<T> : GraphStage<SourceShape<T>>
    {
        #region Internal classes
        private sealed class Logic : OutGraphStageLogic
        {
            private readonly SingleSource<T> _stage;

            public Logic(SingleSource<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.Outlet, this);
            }

            public override void OnPull()
            {
                Push(_stage.Outlet, _stage._element);
                CompleteStage();
            }
        }
        #endregion

        private readonly T _element;

        /// <summary>
        /// The source outlet.
        /// </summary>
        public readonly Outlet<T> Outlet = new("single.out");

        /// <summary>
        /// Creates a source that emits the supplied element once.
        /// </summary>
        /// <param name="element">The element emitted when the outlet is pulled.</param>
        public SingleSource(T element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
            _element = element;
            Shape = new SourceShape<T>(Outlet);
        }

        /// <summary>
        /// The source shape containing the outlet.
        /// </summary>
        public override SourceShape<T> Shape { get; }

        /// <summary>
        /// Creates the logic that emits the element on demand.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes inherited by this source.</param>
        /// <returns>The stage logic that emits one element and completes.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// Materializes a source asynchronously from a task and exposes its materialized value as a task.
    /// </summary>
    /// <typeparam name="T">The type of elements emitted by the resulting source.</typeparam>
    /// <typeparam name="M">The materialized value type of the resulting source.</typeparam>
    public sealed class TaskFlattenSource<T, M> : GraphStageWithMaterializedValue<SourceShape<T>, Task<M>>
    {
        #region Internal classes

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly TaskFlattenSource<T, M> _stage;
            private readonly TaskCompletionSource<M> _materialized;
            private readonly SubSinkInlet<T> _sinkIn;

            public Logic(TaskFlattenSource<T, M> stage, TaskCompletionSource<M> materialized)
                : base(stage.Shape)
            {
                _stage = stage;
                _materialized = materialized;
                _sinkIn = new SubSinkInlet<T>(this, "TaskFlattenSource.in");

                // initial handler (until task completes)
                SetHandler(stage.Outlet, new LambdaOutHandler(
                    onPull: () => { },
                    onDownstreamFinish: cause =>
                    {
                        if (!_materialized.Task.IsCompleted)
                        {
                            // we used to try to materialize the "inner" source here just to get
                            // the materialized value, but that is not safe and may cause the graph shell
                            // to leak/stay alive after the stage completes
                            _materialized.TrySetException(new StreamDetachedException("Stream cancelled before Source Task completed", cause));
                        }
                        InternalOnDownstreamFinish(cause);
                    }));
            }

            public override void PreStart()
            {
                if (_stage._taskSource.IsCompleted)
                {
                    OnTaskSourceCompleted(_stage._taskSource);
                }
                else
                {
                    var cb = GetAsyncCallback<Task<Source<T, M>>>(OnTaskSourceCompleted);
                    _stage._taskSource.ContinueWith(t => cb(t), TaskContinuationOptions.ExecuteSynchronously);
                }
            }

            public override void PostStop()
            {
                if (!_sinkIn.IsClosed) _sinkIn.Cancel();
            }

            public override void OnPull() => _sinkIn.Pull();

            public override void OnPush() => Push(_stage.Outlet, _sinkIn.Grab());

            public override void OnUpstreamFinish() => CompleteStage();

            private void OnTaskSourceCompleted(Task<Source<T, M>> t)
            {
                try
                {
                    var runnable = Source.FromGraph(t.Result).ToMaterialized(_sinkIn.Sink, Keep.Left);
                    var materializedValue = Interpreter.SubFusingMaterializer.Materialize(runnable, _stage.InitialAttributes);
                    _materialized.TrySetResult(materializedValue);

                    SetHandler(_stage.Outlet, this);
                    _sinkIn.SetHandler(this);

                    if (IsAvailable(_stage.Outlet))
                        _sinkIn.Pull();
                }
                catch (Exception ex)
                {
                    _sinkIn.Cancel();
                    _materialized.TrySetException(ex);
                    FailStage(ex);
                }
            }

            private Exception Flatten(AggregateException exception) =>
                exception.InnerExceptions.Count == 1 ? exception.InnerExceptions[0] : exception;
        }

        #endregion

        private readonly Task<Source<T, M>> _taskSource;

        public readonly Outlet<T> Outlet = new("TaskFlattenSource.out");

        public TaskFlattenSource(Task<Source<T, M>> taskSource)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(taskSource);
            _taskSource = taskSource;
            Shape = new SourceShape<T>(Outlet);
        }

        public override SourceShape<T> Shape { get; }

        protected override Attributes InitialAttributes => DefaultAttributes.TaskFlattenSource;

        public override ILogicAndMaterializedValue<Task<M>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var materialized = TaskEx.NonBlockingTaskCompletionSource<M>();
            return new LogicAndMaterializedValue<Task<M>>(new Logic(this, materialized), materialized.Task);
        }

        public override string ToString() => "TaskFlattenSource";
    }

    /// <summary>
    /// Emits the result of a task after downstream requests an element.
    /// </summary>
    /// <typeparam name="T">The type of the task result and emitted element.</typeparam>
    public sealed class TaskSource<T> : GraphStage<SourceShape<T>>
    {
        #region Internal classes
        private sealed class Logic : OutGraphStageLogic
        {
            private readonly TaskSource<T> _stage;

            public Logic(TaskSource<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.Outlet, this);
            }

            public override void OnPull()
            {
                var callback = GetAsyncCallback<Task<T>>(t =>
                {
                    if (!t.IsCanceled && !t.IsFaulted)
                        Emit(_stage.Outlet, t.Result, CompleteStage);
                    else
                        FailStage(t.IsFaulted
                            ? Flatten(t.Exception)
                            : new TaskCanceledException("Task was cancelled."));
                });
                _stage._task.ContinueWith(t => callback(t), TaskContinuationOptions.ExecuteSynchronously);
                SetHandler(_stage.Outlet, EagerTerminateOutput); // After first pull we won't produce anything more
            }

            private Exception Flatten(AggregateException exception)
                => exception.InnerExceptions.Count == 1 ? exception.InnerExceptions[0] : exception;
        }
        #endregion

        private readonly Task<T> _task;

        /// <summary>
        /// The source outlet.
        /// </summary>
        public readonly Outlet<T> Outlet = new("TaskSource.out");

        /// <summary>
        /// Creates a source backed by the supplied task.
        /// </summary>
        /// <param name="task">The task whose result is emitted when the outlet is pulled.</param>
        public TaskSource(Task<T> task)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(task);
            _task = task;
            Shape = new SourceShape<T>(Outlet);
        }

        /// <summary>
        /// The source shape containing the outlet.
        /// </summary>
        public override SourceShape<T> Shape { get; }

        /// <summary>
        /// Creates the logic that emits the task result or propagates the task failure.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes inherited by this source.</param>
        /// <returns>The stage logic that emits the task result.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns the stage name.
        /// </summary>
        /// <returns>The string "TaskSource"</returns>
        public override string ToString() => "TaskSource";
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Discards all received elements.
    /// </summary>
    [InternalApi]
    public sealed class IgnoreSink<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task<Done>>
    {
        #region Internal classes

        private sealed class Logic : InGraphStageLogic
        {
            private readonly IgnoreSink<T> _stage;
            private readonly TaskCompletionSource<Done> _completion;

            public Logic(IgnoreSink<T> stage, TaskCompletionSource<Done> completion) : base(stage.Shape)
            {
                _stage = stage;
                _completion = completion;

                SetHandler(stage.Inlet, this);
            }

            public override void PreStart() => Pull(_stage.Inlet);

            public override void OnPush() => Pull(_stage.Inlet);

            public override void OnUpstreamFinish()
            {
                base.OnUpstreamFinish();
                _completion.TrySetResult(Done.Instance);
            }

            public override void OnUpstreamFailure(Exception e)
            {
                base.OnUpstreamFailure(e);
                if (_completion.TrySetException(e))
                {
                    // See #8209: WatchTermination may keep a different materialized task and discard IgnoreSink's.
                    // Observe the fault here so it cannot resurface later as UnobservedTaskException.
                    _ = _completion.Task.Exception;
                }
            }
        }

        #endregion

        public IgnoreSink() => Shape = new SinkShape<T>(Inlet);

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.IgnoreSink;

        public Inlet<T> Inlet { get; } = new("Ignore.in");

        public override SinkShape<T> Shape { get; }

        public override ILogicAndMaterializedValue<Task<Done>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var completion = TaskEx.NonBlockingTaskCompletionSource<Done>();
            var logic = new Logic(this, completion);
            return new LogicAndMaterializedValue<Task<Done>>(logic, completion.Task);
        }

        public override string ToString() => "IgnoreSink";
    }
};
