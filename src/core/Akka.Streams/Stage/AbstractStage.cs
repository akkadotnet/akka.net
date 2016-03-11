using System;
using System.Reactive.Streams;
using Akka.Streams.Supervision;

namespace Akka.Streams.Stage
{
    internal class PushPullGraphLogic<TIn, TOut> : GraphStageLogic, IDetachedContext<TOut>
    {
        private AbstractStage<TIn, TOut, ISyncDirective, ISyncDirective, IContext<TOut>, ILifecycleContext> _currentStage;
        private readonly FlowShape<TIn, TOut> _shape;

        public PushPullGraphLogic(
            FlowShape<TIn, TOut> shape,
            Attributes attributes,
            AbstractStage<TIn, TOut, ISyncDirective, ISyncDirective, IContext<TOut>, ILifecycleContext> stage)
            : base(shape)
        {
            Attributes = attributes;
            _currentStage = Stage = stage;
            _shape = shape;

            SetHandler(_shape.Inlet, onPush: () =>
            {
                try
                {
                    _currentStage.OnPush(Grab(_shape.Inlet), Context);
                }
                catch (Exception e)
                {
                    OnSupervision(e);
                }
            },
            onUpstreamFailure: exception => _currentStage.OnUpstreamFailure(exception, Context),
            onUpstreamFinish: () => _currentStage.OnUpstreamFinish(Context));

            SetHandler(_shape.Outlet, 
                onPull: () => _currentStage.OnPull(Context),
                onDownstreamFinish: () => _currentStage.OnDownstreamFinish(Context));
        }

        public AbstractStage<TIn, TOut, ISyncDirective, ISyncDirective, IContext<TOut>, ILifecycleContext> Stage { get; }
        IMaterializer ILifecycleContext.Materializer => Materializer;
        public Attributes Attributes { get; }
        public IDetachedContext<TOut> Context => this;

        public bool IsFinishing => IsClosed(_shape.Inlet);
        
        public IDownstreamDirective PushAndFinish(object element)
        {
            return PushAndFinish((TOut)element);
        }

        public IDownstreamDirective Push(object element)
        {
            return Push((TOut)element);
        }

        public IUpstreamDirective Pull()
        {
            Pull(_shape.Inlet);
            return null;
        }

        public FreeDirective Finish()
        {
            CompleteStage();
            return null;
        }

        public FreeDirective Fail(Exception cause)
        {
            FailStage(cause);
            return null;
        }

        public ITerminationDirective AbsorbTermination()
        {
            if (IsClosed(_shape.Outlet))
            {
                var exception = new NotSupportedException("It is not allowed to call AbsorbTermination() from onDownstreamFinish.");
                // This MUST be logged here, since the downstream has cancelled, i.e. there is no one to send onError to, the
                // stage is just about to finish so no one will catch it anyway just the interpreter

                Interpreter.Log.Error(exception.Message);
                throw exception;    // We still throw for correctness (although a finish() would also work here)
            }

            if (IsAvailable(_shape.Outlet)) _currentStage.OnPull(Context);
            return null;
        }

        public bool IsHoldingBoth { get; }
        public bool IsHoldingUpstream => !(IsClosed(_shape.Inlet) || HasBeenPulled(_shape.Inlet));
        public bool IsHoldingDownstream => IsAvailable(_shape.Outlet);
        public FreeDirective PushAndPull(object element)
        {
            return PushAndPull((TOut)element);
        }

        public IUpstreamDirective HoldUpstream()
        {
            return null;
        }

        public IDownstreamDirective HoldUpstreamAndPush(object element)
        {
            return HoldUpstreamAndPush((TOut)element);
        }

        public IDownstreamDirective HoldDownstream()
        {
            return null;
        }

        public IDownstreamDirective HoldDownstreamAndPull()
        {
            Pull(_shape.Inlet);
            return null;
        }

        public IDownstreamDirective PushAndFinish(TOut element)
        {
            Push(_shape.Outlet, element);
            CompleteStage();
            return null;
        }

        public IDownstreamDirective Push(TOut element)
        {
            Push(_shape.Outlet, element);
            return null;
        }

        public FreeDirective PushAndPull(TOut element)
        {
            Push(_shape.Outlet, element);
            Pull(_shape.Inlet);
            return null;
        }

        public IDownstreamDirective HoldUpstreamAndPush(TOut element)
        {
            Push(_shape.Outlet, element);
            return null;
        }

        protected internal override void BeforePreStart()
        {
            base.BeforePreStart();
            if (_currentStage.IsDetached) Pull(_shape.Inlet);
        }

        private void OnSupervision(Exception exception)
        {
            var decision = _currentStage.Decide(exception);
            switch (decision)
            {
                case Directive.Stop:
                    FailStage(exception);
                    break;
                case Directive.Resume:
                    ResetAfterSupervise();
                    break;
                case Directive.Restart:
                    ResetAfterSupervise();
                    _currentStage.PostStop();
                    _currentStage = (AbstractStage<TIn, TOut, ISyncDirective, ISyncDirective, IContext<TOut>, ILifecycleContext>)_currentStage.Restart();
                    _currentStage.PreStart(Context);
                    break;
                default:
                    throw new NotSupportedException($"PushPullGraphLogic doesn't support supervision directive {decision}");
            }
        }

        private void ResetAfterSupervise()
        {
            var mustPull = _currentStage.IsDetached || IsAvailable(_shape.Outlet);
            if (!HasBeenPulled(_shape.Inlet) && mustPull) Pull(_shape.Inlet);
        }

        public override string ToString() => $"PushPullGraphLogic({_currentStage})";
    }

    internal class PushPullGraphStageWithMaterializedValue<TIn, TOut, TExt, TMat> : GraphStageWithMaterializedValue<FlowShape<TIn, TOut>, TMat>
    {
        public readonly Func<Attributes, Tuple<IStage<TIn, TOut>, TMat>> Factory;

        public PushPullGraphStageWithMaterializedValue(Func<Attributes, Tuple<IStage<TIn, TOut>, TMat>> factory, Attributes stageAttributes)
        {
            InitialAttributes = stageAttributes;
            Factory = factory;

            var name = stageAttributes.GetNameOrDefault();
            Shape = new FlowShape<TIn, TOut>(new Inlet<TIn>(name + ".in"), new Outlet<TOut>(name + ".out"));
        }

        protected override Attributes InitialAttributes { get; }
        public override FlowShape<TIn, TOut> Shape { get; }

        public override GraphStageLogic CreateLogicAndMaterializedValue(Attributes inheritedAttributes, out TMat materialized)
        {
            var stageAndMat = Factory(inheritedAttributes);
            materialized = stageAndMat.Item2;
            return new PushPullGraphLogic<TIn, TOut>(Shape, inheritedAttributes, (AbstractStage<TIn, TOut, ISyncDirective, ISyncDirective, IContext<TOut>, ILifecycleContext>)stageAndMat.Item1);
        }

        public sealed override string ToString()
        {
            return InitialAttributes.GetNameOrDefault();
        }
    }

    internal class PushPullGraphStage<TIn, TOut, TExt> : PushPullGraphStageWithMaterializedValue<TIn, TOut, TExt, Unit>
    {
        public PushPullGraphStage(Func<Attributes, IStage<TIn, TOut>> factory, Attributes stageAttributes) : base(attributes => Tuple.Create(factory(attributes), Unit.Instance), stageAttributes)
        {
        }
    }

    public abstract class AbstractStage<TIn, TOut, TPushDirective, TPullDirective, TContext, TLifecycleContext> : IStage<TIn, TOut> where TPushDirective : IDirective where TPullDirective : IDirective where TContext : IContext where TLifecycleContext : ILifecycleContext
    {
        protected TContext Context;

        internal protected virtual bool IsDetached => false;
        
        /// <summary>
        /// User overridable callback.
        /// <para>
        /// It is called before any other method defined on the <see cref="IStage{TIn,TOut}"/>.
        /// Empty default implementation.
        /// </para>
        /// </summary>
        public virtual void PreStart(TLifecycleContext context)
        {
        }

        /// <summary>
        /// <para>
        /// This method is called when an element from upstream is available and there is demand from downstream, i.e.
        /// in <see cref="OnPush"/> you are allowed to call <see cref="IContext.Push"/> to emit one element downstreams,
        /// or you can absorb the element by calling <see cref="IContext.Pull"/>. Note that you can only
        /// emit zero or one element downstream from <see cref="OnPull"/>.
        /// </para>
        /// <para>
        /// To emit more than one element you have to push the remaining elements from <see cref="OnPull"/>, one-by-one.
        /// <see cref="OnPush"/> is not called again until <see cref="OnPull"/> has requested more elements with
        /// <see cref="IContext.Pull"/>.
        /// </para>
        /// </summary>
        public abstract TPushDirective OnPush(TIn element, TContext context);

        /// <summary>
        /// This method is called when there is demand from downstream, i.e. you are allowed to push one element
        /// downstreams with <see cref="IContext.Push"/>, or request elements from upstreams with <see cref="IContext.Pull"/>
        /// </summary>
        public abstract TPullDirective OnPull(TContext context);

        /// <summary>
        /// <para>
        /// This method is called when upstream has signaled that the stream is successfully completed. 
        /// Here you cannot call <see cref="IContext.Push"/>, because there might not be any demand from downstream. 
        /// To emit additional elements before terminating you can use <see cref="IContext.AbsorbTermination"/> and push final elements
        /// from <see cref="OnPull"/>. The stage will then be in finishing state, which can be checked
        /// with <see cref="IContext.IsFinishing"/>.
        /// </para>
        /// <para>
        /// By default the finish signal is immediately propagated with <see cref="IContext.Finish"/>.
        /// </para>
        /// <para>
        /// IMPORTANT NOTICE: this signal is not back-pressured, it might arrive from upstream even though
        /// the last action by this stage was a “push”.
        /// </para>
        /// </summary>
        public virtual ITerminationDirective OnUpstreamFinish(TContext context)
        {
            return context.Finish();
        }

        /// <summary>
        /// This method is called when downstream has cancelled. 
        /// By default the cancel signal is immediately propagated with <see cref="IContext.Finish"/>.
        /// </summary>
        public virtual ITerminationDirective OnDownstreamFinish(TContext context)
        {
            return context.Finish();
        }

        /// <summary>
        /// <para>
        /// <see cref="OnUpstreamFailure"/> is called when upstream has signaled that the stream is completed
        /// with failure. It is not called if <see cref="OnPull"/> or <see cref="OnPush"/> of the stage itself
        /// throws an exception.
        /// </para>
        /// <para>
        /// Note that elements that were emitted by upstream before the failure happened might
        /// not have been received by this stage when <see cref="OnUpstreamFailure"/> is called, i.e.
        /// failures are not backpressured and might be propagated as soon as possible.
        /// </para>
        /// <para>
        /// Here you cannot call <see cref="IContext.Push"/>, because there might not
        /// be any demand from  downstream. To emit additional elements before terminating you
        /// can use <see cref="IContext.AbsorbTermination"/> and push final elements
        /// from <see cref="OnPull"/>. The stage will then be in finishing state, which can be checked
        /// with <see cref="IContext.IsFinishing"/>.
        /// </para>
        /// </summary>
        public virtual ITerminationDirective OnUpstreamFailure(Exception cause, TContext context)
        {
            return context.Fail(cause);
        }

        // TODO need better wording here
        /// <summary>
        /// User overridable callback.
        /// Is called after the Stages final action is performed.  
        /// Empty default implementation.
        /// </summary>
        public virtual void PostStop()
        {
        }

        /// <summary>
        /// If an exception is thrown from <see cref="OnPush"/> this method is invoked to decide how
        /// to handle the exception. By default this method returns <see cref="Directive.Stop"/>.
        /// <para>
        /// If an exception is thrown from <see cref="OnPull"/> the stream will always be completed with
        /// failure, because it is not always possible to recover from that state.
        /// In concrete stages it is of course possible to use ordinary try-catch-recover inside
        /// <see cref="OnPull"/> when it is know how to recover from such exceptions.
        /// </para>
        /// </summary> 
        public virtual Directive Decide(Exception cause)
        {
            return Directive.Stop;
        }

        /// <summary>
        /// Used to create a fresh instance of the stage after an error resulting in a <see cref="Directive.Restart"/>
        /// directive. By default it will return the same instance untouched, so you must override it
        /// if there are any state that should be cleared before restarting, e.g. by returning a new instance.
        /// </summary>
        public virtual IStage<TIn, TOut> Restart()
        {
            return this;
        }
    }
}