using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Streams;
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
        internal static SimpleLinearGraphStage<T> Identity<T>() => Implementation.Fusing.Identity<T>.Instance;

        internal static GraphStageWithMaterializedValue<FlowShape<T, T>, Task<Unit>> TerminationWatcher<T>()
            => Implementation.Fusing.TerminationWatcher<T>.Instance;

        /// <summary>
        /// Fusing graphs that have cycles involving FanIn stages might lead to deadlocks if
        /// demand is not carefully managed.
        /// 
        /// This means that FanIn stages need to early pull every relevant input on startup.
        /// This can either be implemented inside the stage itself, or this method can be used,
        /// which adds a detacher stage to every input.
        /// </summary>
        internal static IGraph<UniformFanInShape<T, T>, TMat> WithDetachedInputs<T, TMat>(GraphStage<UniformFanInShape<T, T>> stage)
        {
            return GraphDsl.Create<UniformFanInShape<T, T>, TMat>(builder =>
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

    internal class GraphStageModule : Module
    {
        public readonly IGraphStageWithMaterializedValue Stage;

        public GraphStageModule(Shape shape, Attributes attributes, IGraphStageWithMaterializedValue stage)
        {
            Shape = shape;
            Attributes = attributes;
            Stage = stage;
        }

        public override Shape Shape { get; }

        public override IModule ReplaceShape(Shape shape)
        {
            return new CopiedModule(shape, Attributes.None, this);
        }

        public override ImmutableArray<IModule> SubModules => ImmutableArray<IModule>.Empty;

        public override IModule CarbonCopy()
        {
            return ReplaceShape(Shape.DeepCopy());
        }

        public override Attributes Attributes { get; }

        public override IModule WithAttributes(Attributes attributes)
        {
            return new GraphStageModule(Shape, attributes, Stage);
        }

        public override string ToString() => Stage.ToString();
    }

    internal abstract class SimpleLinearGraphStage<T> : GraphStage<FlowShape<T, T>>
    {
        public readonly Inlet<T> Inlet;
        public readonly Outlet<T> Outlet;
        private readonly FlowShape<T, T> _shape;

        protected SimpleLinearGraphStage()
        {
            var name = GetType().Name;
            Inlet = new Inlet<T>(name + ".in");
            Outlet = new Outlet<T>(name + ".out");
            _shape = new FlowShape<T, T>(Inlet, Outlet);
        }

        public override FlowShape<T, T> Shape { get { return _shape; } }
    }

    internal sealed class Identity<T> : SimpleLinearGraphStage<T>
    {
        #region internal classes
        private sealed class Logic : GraphStageLogic
        {
            public Logic(Shape shape, Identity<T> stage) : base(shape)
            {
                SetHandler(stage.Inlet, () =>
                {
                    Push(stage.Outlet, Grab(stage.Inlet));
                });
                SetHandler(stage.Outlet, () =>
                {
                    Pull(stage.Inlet);
                });
            }
        }
        #endregion

        public static readonly Identity<T> Instance = new Identity<T>();

        private readonly Attributes _initialAttributes;
        private Identity()
        {
            _initialAttributes = Attributes.CreateName("identityOp");
        }

        protected override Attributes InitialAttributes { get { return _initialAttributes; } }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(Shape, this);
        }
    }

    internal sealed class Detacher<T> : GraphStage<FlowShape<T, T>>
    {
        #region internal classes
        private sealed class Logic : GraphStageLogic
        {
            private readonly Detacher<T> _stage;

            public Logic(Shape shape, Detacher<T> stage) : base(shape)
            {
                _stage = stage;
                SetHandler(stage.Inlet,
                    onPush: () =>
                    {
                        var outlet = stage.Outlet;
                        if (IsAvailable(outlet))
                        {
                            var inlet = stage.Inlet;
                            Push(outlet, Grab(inlet));
                            TryPull(inlet);
                        }
                    },
                    onUpstreamFinish: () =>
                    {
                        if (!IsAvailable(stage.Inlet)) CompleteStage();
                    });
                SetHandler(stage.Outlet,
                    onPull: () =>
                    {
                        var inlet = stage.Inlet;
                        if (IsAvailable(inlet))
                        {
                            var outlet = stage.Outlet;
                            Push(outlet, Grab(inlet));
                            if (IsClosed(inlet)) CompleteStage();
                            else Pull(inlet);
                        }
                    });
            }

            public override void PreStart()
            {
                TryPull(_stage.Inlet);
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
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(Shape, this);
        }

        public override string ToString()
        {
            return "Detacher";
        }
    }

    internal sealed class TerminationWatcher<T> : GraphStageWithMaterializedValue<FlowShape<T, T>, Task<Unit>>
    {
        public static readonly TerminationWatcher<T> Instance = new TerminationWatcher<T>();

        #region internal classes 

        private sealed class TerminationWatcherLogic : GraphStageLogic
        {
            public TerminationWatcherLogic(TerminationWatcher<T> watcher, TaskCompletionSource<Unit> finishPromise) : base(watcher.Shape)
            {
                SetHandler(watcher._inlet, onPush: ()=> Push(watcher._outlet, Grab(watcher._inlet)),onUpstreamFinish:()=>
                {
                    finishPromise.TrySetResult(Unit.Instance);
                    CompleteStage();
                }, onUpstreamFailure: ex=>
                {
                    finishPromise.TrySetException(ex);
                    FailStage(ex);
                });

                SetHandler(watcher._outlet, onPull: ()=> Pull(watcher._inlet), onDownstreamFinish: ()=> 
                {
                    finishPromise.TrySetResult(Unit.Instance);
                    CompleteStage();
                });
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

        public override GraphStageLogic CreateLogicAndMaterializedValue(Attributes inheritedAttributes, out Task<Unit> materialized)
        {
            var finishPromise = new TaskCompletionSource<Unit>();
            materialized = finishPromise.Task;
            return new TerminationWatcherLogic(this, finishPromise);
        }

        public override string ToString() => "TerminationWatcher";
    }

    internal sealed class TickSource<T> : GraphStageWithMaterializedValue<SourceShape<T>, ICancelable>
    {
        #region internal classes
        private sealed class TickSourceCancellable : ICancelable
        {
            internal readonly AtomicBoolean Cancelled;
            private readonly TaskCompletionSource<Unit> _cancelPromise = new TaskCompletionSource<Unit>();

            public TickSourceCancellable(AtomicBoolean cancelled)
            {
                Cancelled = cancelled;
            }

            public void Cancel()
            {
                if (!IsCancellationRequested) _cancelPromise.SetResult(Unit.Instance);
            }

            public bool IsCancellationRequested { get { return Cancelled.Value; } }
            public CancellationToken Token { get; }

            public Task CancelTask { get { return _cancelPromise.Task; } }

            public void CancelAfter(TimeSpan delay)
            {
                Task.Delay(delay).ContinueWith(_ => Cancel(true));
            }

            public void CancelAfter(int millisecondsDelay)
            {
                Task.Delay(millisecondsDelay).ContinueWith(_ => Cancel(true));
            }

            public void Cancel(bool throwOnFirstException)
            {
                if (!IsCancellationRequested) _cancelPromise.SetResult(Unit.Instance);
            }
        }

        private sealed class TickStageLogic : TimerGraphStageLogic
        {
            private readonly TickSourceCancellable _cancelable;
            private readonly TickSource<T> _stage;

            public TickStageLogic(Shape shape, TickSourceCancellable cancelable, TickSource<T> stage) : base(shape)
            {
                _cancelable = cancelable;
                _stage = stage;

                SetHandler(_stage.Outlet, EagerTerminateOutput);
            }

            public override void PreStart()
            {
                ScheduleRepeatedly("TickTimer", _stage._initialDelay, _stage._interval);
                var callback = GetAsyncCallback<Unit>(_ =>
                {
                    CompleteStage();
                    _cancelable.Cancelled.Value = true;
                });

                _cancelable.CancelTask.ContinueWith(t => callback(Unit.Instance), TaskContinuationOptions.AttachedToParent | TaskContinuationOptions.ExecuteSynchronously);
            }

            protected internal override void OnTimer(object timerKey)
            {
                if (IsAvailable(_stage.Outlet)) Push(_stage.Outlet, _stage._tick);
            }
        }
        #endregion

        private readonly TimeSpan _initialDelay;
        private readonly TimeSpan _interval;
        private readonly T _tick;

        private readonly Outlet<T> Outlet;

        public TickSource(TimeSpan initialDelay, TimeSpan interval, T tick)
        {
            _initialDelay = initialDelay;
            _interval = interval;
            _tick = tick;
            Outlet = new Outlet<T>("TimerSource.out");
            InitialAttributes = Attributes.CreateName("TickSource");
            Shape = new SourceShape<T>(Outlet);
        }

        protected override Attributes InitialAttributes { get; }
        public override SourceShape<T> Shape { get; }
        public override GraphStageLogic CreateLogicAndMaterializedValue(Attributes inheritedAttributes, out ICancelable cancellable)
        {
            var cancelled = new AtomicBoolean(false);
            var c = new TickSourceCancellable(cancelled);
            var logic = new TickStageLogic(Shape, c, this);
            cancellable = c;
            return logic;
        }
    }

    public interface IMaterializedValueSource
    {
        IMaterializedValueSource CopySource();
        Outlet Outlet { get; }
        StreamLayout.IMaterializedValueNode Computation { get; }
        void SetValue(object result);
    }

    internal sealed class MaterializedValueSource<T> : GraphStage<SourceShape<T>>, IMaterializedValueSource
    {
        #region internal classes
        internal sealed class MaterializedValueGraphStageLogic : GraphStageLogic
        {
            private readonly MaterializedValueSource<T> _source;

            public MaterializedValueGraphStageLogic(Shape shape, MaterializedValueSource<T> source) : base(shape)
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

        protected override Attributes InitialAttributes { get { return Name; } }
        public override SourceShape<T> Shape { get; }

        public void SetValue(T value)
        {
            _promise.SetResult(value);
        }

        void IMaterializedValueSource.SetValue(object result)
        {
            SetValue((T)result);
        }

        public MaterializedValueSource<T> CopySource()
        {
            return new MaterializedValueSource<T>(Computation, Outlet);
        }

        IMaterializedValueSource IMaterializedValueSource.CopySource()
        {
            return CopySource();
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new MaterializedValueGraphStageLogic(Shape, this);
        }
    }

    internal sealed class SingleSource<T> : GraphStage<SourceShape<T>>
    {
        #region Internal classes
        private sealed class AnonymousStageLogic : GraphStageLogic
        {
            public AnonymousStageLogic(Shape shape, SingleSource<T> source) : base(shape)
            {
                SetHandler(source.Outlet, onPull: () =>
                {
                    Push(source.Outlet, source._element);
                    CompleteStage();
                });
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
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new AnonymousStageLogic(Shape, this);
        }
    }

    internal sealed class TaskSource<T> : GraphStage<SourceShape<T>>
    {
        #region Internal classes
        private sealed class Logic : GraphStageLogic
        {
            public Logic(Shape shape, TaskSource<T> source) : base(shape)
            {
                SetHandler(source.Outlet, onPull: () =>
                {
                    var callback = GetAsyncCallback<Task<T>>(t =>
                    {
                        if (!t.IsCanceled && !t.IsFaulted)
                            Emit(source.Outlet, t.Result, CompleteStage);
                        else
                            FailStage(t.IsFaulted
                                ? Flatten(t.Exception)
                                : new TaskCanceledException("Task was cancelled."));
                    });
                    source._task.ContinueWith(t => callback(t), TaskContinuationOptions.ExecuteSynchronously);
                    SetHandler(source.Outlet, EagerTerminateOutput); // After first pull we won't produce anything more
                });
            }

            private Exception Flatten(AggregateException exception)
            {
                return exception.InnerExceptions.Count == 1 ? exception.InnerExceptions[0] : exception;
            }
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

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(Shape, this);
        }

        public override string ToString()
        {
            return "TaskSource";
        }
    }
}