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

    internal class GraphStageModule<TMat> : Module
    {
        public readonly GraphStageWithMaterializedValue<Shape, TMat> Stage;
        private readonly Shape _shape;
        private readonly Attributes _attributes;

        public GraphStageModule(Shape shape, Attributes attributes, GraphStageWithMaterializedValue<Shape, TMat> stage)
        {
            _shape = shape;
            _attributes = attributes;
            Stage = stage;
        }

        public override Shape Shape { get { return _shape; } }
        public override IModule ReplaceShape(Shape shape)
        {
            return new CopiedModule(shape, Attributes.None, this);
        }

        public override IImmutableSet<IModule> SubModules { get { return ImmutableHashSet<IModule>.Empty; } }
        public override IModule CarbonCopy()
        {
            return ReplaceShape(Shape.DeepCopy());
        }

        public override Attributes Attributes { get { return _attributes; } }
        public override IModule WithAttributes(Attributes attributes)
        {
            return new GraphStageModule<TMat>(Shape, attributes, Stage);
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
            private readonly AtomicBoolean _cancelled;
            private readonly TaskCompletionSource<Unit> _cancelPromise = new TaskCompletionSource<Unit>();

            public TickSourceCancellable(AtomicBoolean cancelled)
            {
                _cancelled = cancelled;
            }

            public void Cancel()
            {
                if (!IsCancellationRequested) _cancelPromise.SetResult(Unit.Instance);
            }

            public bool IsCancellationRequested { get { return _cancelled.Value; } }
            public CancellationToken Token { get; }
            public void CancelAfter(TimeSpan delay)
            {
                throw new NotImplementedException();
            }

            public void CancelAfter(int millisecondsDelay)
            {
                throw new NotImplementedException();
            }

            public void Cancel(bool throwOnFirstException)
            {

            }
        }
        #endregion

        private readonly Outlet<T> Outlet;

        public TickSource(TimeSpan initialDelay, TimeSpan interval, T tick)
        {
            Outlet = new Outlet<T>("TimerSource.out");
            InitialAttributes = Attributes.CreateName("TickSource");
            Shape = new SourceShape<T>(Outlet);
        }

        protected override Attributes InitialAttributes { get; }
        public override SourceShape<T> Shape { get; }
        public override GraphStageLogic CreateLogicAndMaterializedValue(Attributes inheritedAttributes, out ICancelable cancellable)
        {
            var cancelled = new AtomicBoolean(false);
            cancellable = new TickSourceCancellable(cancelled);
            var logic = new TimerGraphStageLogic(Shape, cancellable, this);
            return logic;
        }
    }

    internal class MaterializedValueSource<T> : GraphStage<SourceShape<T>>, ICloneable
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
                var cb = GetAsyncCallback<T>(element => Emit(_source.Outlet, element, CompleteStage<T>));
                _source._promise.Task.ContinueWith(task => cb(task.Result), TaskContinuationOptions.ExecuteSynchronously);
            }
        }
        #endregion

        private static readonly Attributes Name = Attributes.CreateName("matValueSource");

        public readonly StreamLayout.IMaterializedValueNode Computation;
        public readonly Outlet<T> Outlet;

        private readonly TaskCompletionSource<T> _promise = new TaskCompletionSource<T>();

        public MaterializedValueSource(StreamLayout.IMaterializedValueNode computation, Outlet<T> outlet)
        {
            Computation = computation;
            Outlet = outlet;
            Shape = new SourceShape<T>(Outlet);
        }

        protected override Attributes InitialAttributes { get { return Name; } }
        public override SourceShape<T> Shape { get; }

        public void SetValue(T value)
        {
            _promise.SetResult(value);
        }

        public MaterializedValueSource<T> CopySource()
        {
            return new MaterializedValueSource<T>(Computation, Outlet);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new MaterializedValueGraphStageLogic(Shape, this);
        }

        public object Clone()
        {
            return CopySource();
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
                    CompleteStage<T>();
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