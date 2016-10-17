//-----------------------------------------------------------------------
// <copyright file="GraphStages.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Util;

namespace Akka.Streams.Implementation.Fusing
{
    public static class GraphStages
    {
        public static SimpleLinearGraphStage<T> Identity<T>() => Implementation.Fusing.Identity<T>.Instance;

        internal static GraphStageWithMaterializedValue<FlowShape<T, T>, Task> TerminationWatcher<T>()
            => Implementation.Fusing.TerminationWatcher<T>.Instance;

        /// <summary>
        /// Fusing graphs that have cycles involving FanIn stages might lead to deadlocks if
        /// demand is not carefully managed.
        /// 
        /// This means that FanIn stages need to early pull every relevant input on startup.
        /// This can either be implemented inside the stage itself, or this method can be used,
        /// which adds a detacher stage to every input.
        /// </summary>
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
    public class GraphStageModule : AtomicModule
    {
        public readonly IGraphStageWithMaterializedValue<Shape, object> Stage;

        public GraphStageModule(Shape shape, Attributes attributes, IGraphStageWithMaterializedValue<Shape, object> stage)
        {
            Shape = shape;
            Attributes = attributes;
            Stage = stage;
        }

        public override Shape Shape { get; }

        public override IModule ReplaceShape(Shape shape) => new CopiedModule(shape, Attributes.None, this);

        public override IModule CarbonCopy() => ReplaceShape(Shape.DeepCopy());

        public override Attributes Attributes { get; }

        public override IModule WithAttributes(Attributes attributes) => new GraphStageModule(Shape, attributes, Stage);

        public override string ToString() => $"GraphStage({Stage}) [{GetHashCode()}%08x]";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public abstract class SimpleLinearGraphStage<T> : GraphStage<FlowShape<T, T>>
    {
        public readonly Inlet<T> Inlet;
        public readonly Outlet<T> Outlet;

        protected SimpleLinearGraphStage()
        {
            var name = GetType().Name;
            Inlet = new Inlet<T>(name + ".in");
            Outlet = new Outlet<T>(name + ".out");
            Shape = new FlowShape<T, T>(Inlet, Outlet);
        }

        public override FlowShape<T, T> Shape { get; }
    }

    public sealed class Identity<T> : SimpleLinearGraphStage<T>
    {
        #region internal classes
        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Identity<T> _stage;

            public Logic(Identity<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.Inlet, this);
                SetHandler(stage.Outlet, this);
            }

            public override void OnPush() => Push(_stage.Outlet, Grab(_stage.Inlet));

            public override void OnPull() => Pull(_stage.Inlet);
        }
        #endregion

        public static readonly Identity<T> Instance = new Identity<T>();

        private Identity()
        {
        }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("identityOp");

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class Detacher<T> : GraphStage<FlowShape<T, T>>
    {
        #region internal classes
        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Detacher<T> _stage;

            public Logic(Detacher<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.Inlet, this);
                SetHandler(stage.Outlet, this);
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

        public readonly Inlet<T> Inlet;

        public readonly Outlet<T> Outlet;

        public Detacher()
        {
            InitialAttributes = Attributes.CreateName("Detacher");
            Inlet = new Inlet<T>("in");
            Outlet = new Outlet<T>("out");
            Shape = new FlowShape<T, T>(Inlet, Outlet);
        }

        protected override Attributes InitialAttributes { get; }

        public override FlowShape<T, T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "Detacher";
    }

    internal sealed class TerminationWatcher<T> : GraphStageWithMaterializedValue<FlowShape<T, T>, Task>
    {
        public static readonly TerminationWatcher<T> Instance = new TerminationWatcher<T>();

        #region internal classes 

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly TerminationWatcher<T> _stage;
            private readonly TaskCompletionSource<NotUsed> _finishPromise;

            public Logic(TerminationWatcher<T> stage, TaskCompletionSource<NotUsed> finishPromise) : base(stage.Shape)
            {
                _stage = stage;
                _finishPromise = finishPromise;

                SetHandler(stage._inlet, this);
                SetHandler(stage._outlet, this);
            }

            public override void OnPush() => Push(_stage._outlet, Grab(_stage._inlet));

            public override void OnUpstreamFinish()
            {
                _finishPromise.TrySetResult(NotUsed.Instance);
                CompleteStage();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                _finishPromise.TrySetException(e);
                FailStage(e);
            }

            public override void OnPull() => Pull(_stage._inlet);

            public override void OnDownstreamFinish()
            {
                _finishPromise.TrySetResult(NotUsed.Instance);
                CompleteStage();
            }
        }

        #endregion

        private readonly Inlet<T> _inlet = new Inlet<T>("terminationWatcher.in");
        private readonly Outlet<T> _outlet = new Outlet<T>("terminationWatcher.out");

        private TerminationWatcher()
        {
            Shape = new FlowShape<T, T>(_inlet, _outlet);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.TerminationWatcher;

        public override FlowShape<T, T> Shape { get; }

        public override ILogicAndMaterializedValue<Task> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var finishPromise = new TaskCompletionSource<NotUsed>();
            return new LogicAndMaterializedValue<Task>(new Logic(this, finishPromise), finishPromise.Task);
        }

        public override string ToString() => "TerminationWatcher";
    }

    internal sealed class FLowMonitorImpl<T> : AtomicReference<object>, IFlowMonitor
    {
        public FLowMonitorImpl() : base(FlowMonitor.Initialized.Instance)
        {
            
        }

        public FlowMonitor.IStreamState State
        {
            get
            {
                var value = Value;
                if(value is T)
                    return new FlowMonitor.Received<T>((T)value);
                
                return value as FlowMonitor.IStreamState;
            }
        }
    }

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

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
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

            public override void OnDownstreamFinish()
            {
                CompleteStage();
                _monitor.Value = FlowMonitor.Finished.Instance;
            }

            public override string ToString() => "MonitorFlowLogic";
        }

        #endregion

        public MonitorFlow()
        {
            Shape = new FlowShape<T, T>(In, Out);
        }

        public Inlet<T> In { get; } = new Inlet<T>("MonitorFlow.in");

        public Outlet<T> Out { get; } = new Outlet<T>("MonitorFlow.out");

        public override FlowShape<T, T> Shape { get; }

        public override ILogicAndMaterializedValue<IFlowMonitor> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var monitor = new FLowMonitorImpl<T>();
            var logic = new Logic(this, monitor);
            return new LogicAndMaterializedValue<IFlowMonitor>(logic, monitor);
        }

        public override string ToString() => "MonitorFlow";
    }

    public sealed class TickSource<T> : GraphStageWithMaterializedValue<SourceShape<T>, ICancelable>
    {
        #region internal classes
        
        [SuppressMessage("ReSharper", "MethodSupportsCancellation")]
        private sealed class Logic : TimerGraphStageLogic, ICancelable
        {
            private readonly TickSource<T> _stage;
            private readonly AtomicBoolean _cancelled = new AtomicBoolean();

            private readonly AtomicReference<Action<NotUsed>> _cancelCallback =
                new AtomicReference<Action<NotUsed>>(null);

            public Logic(TickSource<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(_stage.Out, EagerTerminateOutput);
            }

            public override void PreStart()
            {
                _cancelCallback.Value = GetAsyncCallback<NotUsed>(_ => CompleteStage());

                if(_cancelled)
                    CompleteStage();
                else
                    ScheduleRepeatedly("TickTimer", _stage._initialDelay, _stage._interval);
            }

            protected internal override void OnTimer(object timerKey)
            {
                if (IsAvailable(_stage.Out) && !_cancelled)
                    Push(_stage.Out, _stage._tick);
            }

            public void Cancel()
            {
                if(!_cancelled.GetAndSet(true))
                    _cancelCallback.Value?.Invoke(NotUsed.Instance);
            }

            public bool IsCancellationRequested => _cancelled;

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

        public TickSource(TimeSpan initialDelay, TimeSpan interval, T tick)
        {
            _initialDelay = initialDelay;
            _interval = interval;
            _tick = tick;
            Shape = new SourceShape<T>(Out);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.TickSource;

        public Outlet<T> Out { get; } = new Outlet<T>("TimerSource.out");

        public override SourceShape<T> Shape { get; }

        public override ILogicAndMaterializedValue<ICancelable> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this);
            return new LogicAndMaterializedValue<ICancelable>(logic, logic);
        }

        public override string ToString() => $"TickSource({_initialDelay}, {_interval}, {_tick})";
    }

    public interface IMaterializedValueSource
    {
        IModule Module { get; }
        IMaterializedValueSource CopySource();
        Outlet Outlet { get; }
        StreamLayout.IMaterializedValueNode Computation { get; }
        void SetValue(object result);
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// This source is not reusable, it is only created internally.
    /// </summary>
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

        public StreamLayout.IMaterializedValueNode Computation { get; }

        Outlet IMaterializedValueSource.Outlet => Outlet;

        public readonly Outlet<T> Outlet;

        private readonly TaskCompletionSource<T> _promise = new TaskCompletionSource<T>();

        public MaterializedValueSource(StreamLayout.IMaterializedValueNode computation, Outlet<T> outlet)
        {
            Computation = computation;
            Outlet = outlet;
            Shape = new SourceShape<T>(Outlet);
        }

        public MaterializedValueSource(StreamLayout.IMaterializedValueNode computation) : this(computation, new Outlet<T>("matValue")) { }

        protected override Attributes InitialAttributes => Name;

        public override SourceShape<T> Shape { get; }

        public void SetValue(T value) => _promise.SetResult(value);

        void IMaterializedValueSource.SetValue(object result) => SetValue((T)result);

        public MaterializedValueSource<T> CopySource() => new MaterializedValueSource<T>(Computation, Outlet);

        IMaterializedValueSource IMaterializedValueSource.CopySource() => CopySource();

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => $"MaterializedValueSource({Computation})";
    }

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

        public readonly Outlet<T> Outlet = new Outlet<T>("single.out");

        public SingleSource(T element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
            _element = element;
            Shape = new SourceShape<T>(Outlet);
        }

        public override SourceShape<T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

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

        public readonly Outlet<T> Outlet = new Outlet<T>("task.out");

        public TaskSource(Task<T> task)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(task);
            _task = task;
            Shape = new SourceShape<T>(Outlet);
        }

        public override SourceShape<T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "TaskSource";
    }
}