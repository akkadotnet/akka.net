using System;
using System.Collections.Immutable;
using System.Reactive.Streams;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Stage;
using Akka.Util;

namespace Akka.Streams.Implementation.Fusing
{
    public static class GraphStages
    {
        internal static SimpleLinearGraphStage<T> Identity<T>()
        {
            return Akka.Streams.Implementation.Fusing.Identity<T>.Instance;
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

        public override string ToString()
        {
            return Stage.ToString();
        }
    }

    internal abstract class SimpleLinearGraphStage<T> : GraphStage<FlowShape<T, T>>
    {
        public readonly Inlet<T> Inlet;
        public readonly Outlet<T> Outlet;
        private readonly FlowShape<T, T> _shape;

        protected SimpleLinearGraphStage()
        {
            var name = this.GetType().Name;
            Inlet = new Inlet<T>(name + ".in");
            Outlet = new Outlet<T>(name + ".out");
            _shape = new FlowShape<T, T>(Inlet, Outlet);
        }

        public override FlowShape<T, T> Shape { get { return _shape; } }
    }

    internal sealed class Identity<T> : SimpleLinearGraphStage<T>
    {
        #region internal classes
        private sealed class AnonymousGraphStageLogic : GraphStageLogic
        {
            public AnonymousGraphStageLogic(Shape shape, Identity<T> stage) : base(shape)
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
            return new AnonymousGraphStageLogic(Shape, this);
        }
    }

    internal sealed class Detacher<T> : GraphStage<FlowShape<T, T>>
    {
        #region internal classes
        private sealed class AnonymousGraphStageLogic : GraphStageLogic
        {
            public AnonymousGraphStageLogic(Shape shape, Detacher<T> stage) : base(shape)
            {
                SetHandler(stage.Inlet, () =>
                {
                    var inlet = stage.Inlet;
                    var outlet = stage.Outlet;
                    if (IsAvailable(outlet))
                    {
                        Push(outlet, Grab(inlet));
                        TryPull(inlet);
                    }
                });
                SetHandler(stage.Outlet, () =>
                {
                    var inlet = stage.Inlet;
                    var outlet = stage.Outlet;
                    if (IsAvailable(inlet))
                    {
                        Push(outlet, Grab(inlet));
                        TryPull(inlet);
                    }
                });
            }
        }
        #endregion

        public readonly Inlet<T> Inlet;
        public readonly Outlet<T> Outlet;
        private Attributes _initialAttributes;

        public Detacher()
        {
            _initialAttributes = Attributes.CreateName("Detacher");
            Inlet = new Inlet<T>("in");
            Outlet = new Outlet<T>("out");
            Shape = new FlowShape<T, T>(Inlet, Outlet);
        }

        protected override Attributes InitialAttributes { get { return _initialAttributes; } }
        public override FlowShape<T, T> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new AnonymousGraphStageLogic(Shape, this);
        }
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

    internal class MaterializedValueSource<T> : GraphStage<SourceShape<T>>, IMaterializedValueSource
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

        public MaterializedValueSource(StreamLayout.IMaterializedValueNode computation) : this(computation, new Outlet<T>("MaterializedValue.out")) { }

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
}