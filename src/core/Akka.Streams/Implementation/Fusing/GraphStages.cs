//-----------------------------------------------------------------------
// <copyright file="GraphStages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

namespace Akka.Streams.Implementation.Fusing
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class GraphStages
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
        public static SimpleLinearGraphStage<T> Identity<T>() => Implementation.Fusing.Identity<T>.Instance;

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
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
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="stage">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        public readonly IGraphStageWithMaterializedValue<Shape, object> Stage;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="stage">TBD</param>
        public GraphStageModule(Shape shape, Attributes attributes, IGraphStageWithMaterializedValue<Shape, object> stage)
        {
            Shape = shape;
            Attributes = attributes;
            Stage = stage;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Shape Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        public override IModule ReplaceShape(Shape shape) => new CopiedModule(shape, Attributes.None, this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override IModule CarbonCopy() => ReplaceShape(Shape.DeepCopy());

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes) => new GraphStageModule(Shape, attributes, Stage);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"GraphStage({Stage}) [{GetHashCode()}%08x]";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public abstract class SimpleLinearGraphStage<T> : GraphStage<FlowShape<T, T>>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T> Inlet;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T> Outlet;

        /// <summary>
        /// TBD
        /// </summary>
        protected SimpleLinearGraphStage(string name = null)
        {
            name = name ?? GetType().Name;
            Inlet = new Inlet<T>(name + ".in");
            Outlet = new Outlet<T>(name + ".out");
            Shape = new FlowShape<T, T>(Inlet, Outlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<T, T> Shape { get; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public sealed class Identity<T> : SimpleLinearGraphStage<T>
    {
        #region internal classes
        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Identity<T> _stage;

            public Logic(Identity<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.Inlet, stage.Outlet, this);
            }

            public override void OnPush() => Push(_stage.Outlet, Grab(_stage.Inlet));

            public override void OnPull() => Pull(_stage.Inlet);
        }
        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Identity<T> Instance = new Identity<T>();

        private Identity()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("identityOp");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
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

                SetHandler(stage.Inlet, stage.Outlet, this);
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
        /// TBD
        /// </summary>
        public Detacher() : base("Detacher")
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("Detacher");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "Detacher";
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal sealed class TerminationWatcher<T> : GraphStageWithMaterializedValue<FlowShape<T, T>, Task>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly TerminationWatcher<T> Instance = new TerminationWatcher<T>();

        #region internal classes 

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly TerminationWatcher<T> _stage;
            private readonly TaskCompletionSource<NotUsed> _finishPromise;
            private bool _completedSignalled;

            public Logic(TerminationWatcher<T> stage, TaskCompletionSource<NotUsed> finishPromise) : base(stage.Shape)
            {
                _stage = stage;
                _finishPromise = finishPromise;

                SetHandler(stage._inlet, stage._outlet, this);
            }

            public override void OnPush() => Push(_stage._outlet, Grab(_stage._inlet));

            public override void OnUpstreamFinish()
            {
                _finishPromise.TrySetResult(NotUsed.Instance);
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

            public override void OnDownstreamFinish()
            {
                _finishPromise.TrySetResult(NotUsed.Instance);
                _completedSignalled = true;
                CompleteStage();
            }

            public override void PostStop()
            {
                if (!_completedSignalled)
                    _finishPromise.TrySetException(new AbruptStageTerminationException(this));
            }
        }

        #endregion

        private readonly Inlet<T> _inlet = new Inlet<T>("TerminationWatcher.in");
        private readonly Outlet<T> _outlet = new Outlet<T>("TerminationWatcher.out");

        private TerminationWatcher()
        {
            Shape = new FlowShape<T, T>(_inlet, _outlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.TerminationWatcher;

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<T, T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Task> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var finishPromise = new TaskCompletionSource<NotUsed>();
            return new LogicAndMaterializedValue<Task>(new Logic(this, finishPromise), finishPromise.Task);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "TerminationWatcher";
    }

    // TODO: fix typo
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal sealed class FLowMonitorImpl<T> : AtomicReference<object>, IFlowMonitor
    {
        /// <summary>
        /// TBD
        /// </summary>
        public FLowMonitorImpl() : base(FlowMonitor.Initialized.Instance)
        {
            
        }

        /// <summary>
        /// TBD
        /// </summary>
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

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
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

                SetHandler(stage.In, stage.Out, this);
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

            public override void PostStop()
            {
                if (!(_monitor.State is FlowMonitor.Finished) && !(_monitor.State is FlowMonitor.Failed))
                    _monitor.Value = new FlowMonitor.Failed(new AbruptStageTerminationException(this));
            }

            public override string ToString() => "MonitorFlowLogic";
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        public MonitorFlow()
        {
            Shape = new FlowShape<T, T>(In, Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<T> In { get; } = new Inlet<T>("MonitorFlow.in");

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<T> Out { get; } = new Outlet<T>("MonitorFlow.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<T, T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<IFlowMonitor> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var monitor = new FLowMonitorImpl<T>();
            var logic = new Logic(this, monitor);
            return new LogicAndMaterializedValue<IFlowMonitor>(logic, monitor);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "MonitorFlow";
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="tick">TBD</param>
        public TickSource(TimeSpan initialDelay, TimeSpan interval, T tick)
        {
            _initialDelay = initialDelay;
            _interval = interval;
            _tick = tick;
            Shape = new SourceShape<T>(Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.TickSource;

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<T> Out { get; } = new Outlet<T>("TimerSource.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<ICancelable> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this);
            return new LogicAndMaterializedValue<ICancelable>(logic, logic);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"TickSource({_initialDelay}, {_interval}, {_tick})";
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IMaterializedValueSource
    {
        /// <summary>
        /// TBD
        /// </summary>
        IModule Module { get; }
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        IMaterializedValueSource CopySource();
        /// <summary>
        /// TBD
        /// </summary>
        Outlet Outlet { get; }
        /// <summary>
        /// TBD
        /// </summary>
        StreamLayout.IMaterializedValueNode Computation { get; }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="result">TBD</param>
        void SetValue(object result);
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// This source is not reusable, it is only created internally.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
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
        /// TBD
        /// </summary>
        public StreamLayout.IMaterializedValueNode Computation { get; }

        Outlet IMaterializedValueSource.Outlet => Outlet;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T> Outlet;

        private readonly TaskCompletionSource<T> _promise = new TaskCompletionSource<T>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="computation">TBD</param>
        /// <param name="outlet">TBD</param>
        public MaterializedValueSource(StreamLayout.IMaterializedValueNode computation, Outlet<T> outlet)
        {
            Computation = computation;
            Outlet = outlet;
            Shape = new SourceShape<T>(Outlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="computation">TBD</param>
        public MaterializedValueSource(StreamLayout.IMaterializedValueNode computation) : this(computation, new Outlet<T>("matValue")) { }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes => Name;

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        public void SetValue(T value) => _promise.SetResult(value);

        void IMaterializedValueSource.SetValue(object result) => SetValue((T)result);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public MaterializedValueSource<T> CopySource() => new MaterializedValueSource<T>(Computation, Outlet);

        IMaterializedValueSource IMaterializedValueSource.CopySource() => CopySource();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"MaterializedValueSource({Computation})";
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
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
        /// TBD
        /// </summary>
        public readonly Outlet<T> Outlet = new Outlet<T>("single.out");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public SingleSource(T element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
            _element = element;
            Shape = new SourceShape<T>(Outlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
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
        /// TBD
        /// </summary>
        public readonly Outlet<T> Outlet = new Outlet<T>("task.out");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="task">TBD</param>
        public TaskSource(Task<T> task)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(task);
            _task = task;
            Shape = new SourceShape<T>(Outlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "TaskSource";
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Discards all received elements.
    /// </summary>
    [InternalApi]
    public sealed class IgnoreSink<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task>
    {
        #region Internal classes

        private sealed class Logic : InGraphStageLogic
        {
            private readonly IgnoreSink<T> _stage;
            private readonly TaskCompletionSource<int> _completion;

            public Logic(IgnoreSink<T> stage, TaskCompletionSource<int> completion) : base(stage.Shape)
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
                _completion.TrySetResult(0);
            }

            public override void OnUpstreamFailure(Exception e)
            {
                base.OnUpstreamFailure(e);
                _completion.TrySetException(e);
            }
        }

        #endregion

        public IgnoreSink()
        {
            Shape = new SinkShape<T>(Inlet);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.IgnoreSink;

        public Inlet<T> Inlet { get; } = new Inlet<T>("Ignore.in");

        public override SinkShape<T> Shape { get; }

        public override ILogicAndMaterializedValue<Task> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var completion = new TaskCompletionSource<int>();
            var logic = new Logic(this, completion);
            return new LogicAndMaterializedValue<Task>(logic, completion.Task);
        }

        public override string ToString() => "IgnoreSink";
    }
};
