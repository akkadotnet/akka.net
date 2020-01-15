//-----------------------------------------------------------------------
// <copyright file="AbstractStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Directive = Akka.Streams.Supervision.Directive;

namespace Akka.Streams.Stage
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    internal class PushPullGraphLogic<TIn, TOut> : GraphStageLogic, IDetachedContext<TOut>
    {
        private AbstractStage<TIn, TOut> _currentStage;
        private readonly FlowShape<TIn, TOut> _shape;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="stage">TBD</param>
        public PushPullGraphLogic(
            FlowShape<TIn, TOut> shape,
            Attributes attributes,
            AbstractStage<TIn, TOut> stage)
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

        /// <summary>
        /// TBD
        /// </summary>
        public AbstractStage<TIn, TOut> Stage { get; }

        IMaterializer ILifecycleContext.Materializer => Materializer;

        /// <summary>
        /// TBD
        /// </summary>
        public Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IDetachedContext<TOut> Context => this;

        /// <summary>
        /// TBD
        /// </summary>
        protected internal override void BeforePreStart()
        {
            base.BeforePreStart();
            if (_currentStage.IsDetached)
                Pull(_shape.Inlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public IDownstreamDirective Push(object element) => Push((TOut)element);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public IDownstreamDirective Push(TOut element)
        {
            Push(_shape.Outlet, element);
            return null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IUpstreamDirective Pull()
        {
            Pull(_shape.Inlet);
            return null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public FreeDirective Finish()
        {
            CompleteStage();
            return null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public IDownstreamDirective PushAndFinish(object element) => PushAndFinish((TOut) element);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public IDownstreamDirective PushAndFinish(TOut element)
        {
            Push(_shape.Outlet, element);
            CompleteStage();
            return null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <returns>TBD</returns>
        public FreeDirective Fail(Exception cause)
        {
            FailStage(cause);
            return null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsFinishing => IsClosed(_shape.Inlet);

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown when the <see cref="FlowShape{TIn,TOut}.Outlet"/> is closed.
        /// </exception>
        /// <returns>TBD</returns>
        public ITerminationDirective AbsorbTermination()
        {
            if (IsClosed(_shape.Outlet))
            {
                var exception = new NotSupportedException("It is not allowed to call AbsorbTermination() from OnDownstreamFinish.");
                // This MUST be logged here, since the downstream has cancelled, i.e. there is no one to send onError to, the
                // stage is just about to finish so no one will catch it anyway just the interpreter

                Interpreter.Log.Error(exception.Message);
                throw exception;    // We still throw for correctness (although a finish() would also work here)
            }

            if (IsAvailable(_shape.Outlet))
                _currentStage.OnPull(Context);
            return null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public FreeDirective PushAndPull(object element) => PushAndPull((TOut) element);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public FreeDirective PushAndPull(TOut element)
        {
            Push(_shape.Outlet, element);
            Pull(_shape.Inlet);
            return null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public IUpstreamDirective HoldUpstreamAndPush(object element) => HoldUpstreamAndPush((TOut) element);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public IUpstreamDirective HoldUpstreamAndPush(TOut element)
        {
            Push(_shape.Outlet, element);
            return null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IDownstreamDirective HoldDownstreamAndPull()
        {
            Pull(_shape.Inlet);
            return null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsHoldingBoth => IsHoldingUpstream && IsHoldingDownstream;

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsHoldingDownstream => IsAvailable(_shape.Outlet);

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsHoldingUpstream => !(IsClosed(_shape.Inlet) || HasBeenPulled(_shape.Inlet));

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IDownstreamDirective HoldDownstream() => null;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IUpstreamDirective HoldUpstream() => null;

        /// <summary>
        /// TBD
        /// </summary>
        public override void PreStart() => _currentStage.PreStart(Context);

        /// <summary>
        /// TBD
        /// </summary>
        public override void PostStop() => _currentStage.PostStop();

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
                    _currentStage = (AbstractStage<TIn, TOut>)_currentStage.Restart();
                    _currentStage.PreStart(Context);
                    break;
                default:
                    throw new NotSupportedException($"PushPullGraphLogic doesn't support supervision directive {decision}");
            }
        }

        private void ResetAfterSupervise()
        {
            var mustPull = _currentStage.IsDetached || IsAvailable(_shape.Outlet);
            if (!HasBeenPulled(_shape.Inlet) && mustPull)
                Pull(_shape.Inlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"PushPullGraphLogic({_currentStage})";
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    /// <typeparam name="TMat">TBD</typeparam>
    public class PushPullGraphStageWithMaterializedValue<TIn, TOut, TMat> : GraphStageWithMaterializedValue<FlowShape<TIn, TOut>, TMat>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Func<Attributes, (IStage<TIn, TOut>, TMat)> Factory;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="factory">TBD</param>
        /// <param name="stageAttributes">TBD</param>
        public PushPullGraphStageWithMaterializedValue(Func<Attributes, (IStage<TIn, TOut>, TMat)> factory, Attributes stageAttributes)
        {
            InitialAttributes = stageAttributes;
            Factory = factory;

            var name = stageAttributes.GetNameOrDefault();
            Shape = new FlowShape<TIn, TOut>(new Inlet<TIn>(name + ".in"), new Outlet<TOut>(name + ".out"));
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<TMat> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var stageAndMat = Factory(inheritedAttributes);
            return
                new LogicAndMaterializedValue<TMat>(
                    new PushPullGraphLogic<TIn, TOut>(Shape, inheritedAttributes,
                        (AbstractStage<TIn, TOut>) stageAndMat.Item1), stageAndMat.Item2);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public sealed override string ToString() => InitialAttributes.GetNameOrDefault();
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class PushPullGraphStage<TIn, TOut> : PushPullGraphStageWithMaterializedValue<TIn, TOut, NotUsed>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="factory">TBD</param>
        /// <param name="stageAttributes">TBD</param>
        /// <returns>TBD</returns>
        public PushPullGraphStage(Func<Attributes, IStage<TIn, TOut>> factory, Attributes stageAttributes) : base(attributes => (factory(attributes), NotUsed.Instance), stageAttributes)
        {
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [Obsolete("Please use GraphStage instead. [1.1.2]")]
    public abstract class AbstractStage<TIn, TOut> : IStage<TIn, TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        protected internal virtual bool IsDetached => false;
        
        /// <summary>
        /// User overridable callback.
        /// <para>
        /// It is called before any other method defined on the <see cref="IStage{TIn,TOut}"/>.
        /// Empty default implementation.
        /// </para>
        /// </summary>
        /// <param name="context">TBD</param>
        public virtual void PreStart(ILifecycleContext context)
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
        /// <param name="element">TBD</param>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public abstract IDirective OnPush(TIn element, IContext context);

        /// <summary>
        /// This method is called when there is demand from downstream, i.e. you are allowed to push one element
        /// downstreams with <see cref="IContext.Push"/>, or request elements from upstreams with <see cref="IContext.Pull"/>
        /// </summary>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public abstract IDirective OnPull(IContext context);

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
        /// the last action by this stage was a "push".
        /// </para>
        /// </summary>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public abstract ITerminationDirective OnUpstreamFinish(IContext context);

        /// <summary>
        /// This method is called when downstream has cancelled. 
        /// By default the cancel signal is immediately propagated with <see cref="IContext.Finish"/>.
        /// </summary>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public abstract ITerminationDirective OnDownstreamFinish(IContext context);

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
        /// <param name="cause">TBD</param>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public abstract ITerminationDirective OnUpstreamFailure(Exception cause, IContext context);

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
        /// <param name="cause">TBD</param>
        /// <returns>TBD</returns>
        public virtual Directive Decide(Exception cause) => Directive.Stop;

        /// <summary>
        /// Used to create a fresh instance of the stage after an error resulting in a <see cref="Directive.Restart"/>
        /// directive. By default it will return the same instance untouched, so you must override it
        /// if there are any state that should be cleared before restarting, e.g. by returning a new instance.
        /// </summary>
        /// <returns>TBD</returns>
        public virtual IStage<TIn, TOut> Restart() => this;
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    /// <typeparam name="TPushDirective">TBD</typeparam>
    /// <typeparam name="TPullDirective">TBD</typeparam>
    /// <typeparam name="TContext">TBD</typeparam>
    [Obsolete("Please use GraphStage instead. [1.1.2]")]
    public abstract class AbstractStage<TIn, TOut, TPushDirective, TPullDirective, TContext> : AbstractStage<TIn, TOut> where TPushDirective : IDirective where TPullDirective : IDirective where TContext : IContext
    {
        /// <summary>
        /// TBD
        /// </summary>
        protected TContext Context;

        /// <summary>
        /// <para>
        /// This method is called when an element from upstream is available and there is demand from downstream, i.e.
        /// in <see cref="OnPush(TIn,TContext)"/> you are allowed to call <see cref="IContext.Push"/> to emit one element downstreams,
        /// or you can absorb the element by calling <see cref="IContext.Pull"/>. Note that you can only
        /// emit zero or one element downstream from <see cref="OnPull(TContext)"/>.
        /// </para>
        /// <para>
        /// To emit more than one element you have to push the remaining elements from <see cref="OnPull(TContext)"/>, one-by-one.
        /// <see cref="OnPush(TIn,TContext)"/> is not called again until <see cref="OnPull(TContext)"/> has requested more elements with
        /// <see cref="IContext.Pull"/>.
        /// </para>
        /// </summary>
        /// <param name="element">TBD</param>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public abstract TPushDirective OnPush(TIn element, TContext context);

        /// <summary>
        /// <para>
        /// This method is called when an element from upstream is available and there is demand from downstream, i.e.
        /// in <see cref="OnPush(TIn,TContext)"/> you are allowed to call <see cref="IContext.Push"/> to emit one element downstreams,
        /// or you can absorb the element by calling <see cref="IContext.Pull"/>. Note that you can only
        /// emit zero or one element downstream from <see cref="OnPull(TContext)"/>.
        /// </para>
        /// <para>
        /// To emit more than one element you have to push the remaining elements from <see cref="OnPull(TContext)"/>, one-by-one.
        /// <see cref="OnPush(TIn,TContext)"/> is not called again until <see cref="OnPull(TContext)"/> has requested more elements with
        /// <see cref="IContext.Pull"/>.
        /// </para>
        /// </summary>
        /// <param name="element">TBD</param>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public sealed override IDirective OnPush(TIn element, IContext context) => OnPush(element, (TContext) context);

        /// <summary>
        /// This method is called when there is demand from downstream, i.e. you are allowed to push one element
        /// downstreams with <see cref="IContext.Push"/>, or request elements from upstreams with <see cref="IContext.Pull"/>
        /// </summary>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public abstract TPullDirective OnPull(TContext context);


        /// <summary>
        /// This method is called when there is demand from downstream, i.e. you are allowed to push one element
        /// downstreams with <see cref="IContext.Push"/>, or request elements from upstreams with <see cref="IContext.Pull"/>
        /// </summary>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public override IDirective OnPull(IContext context) => OnPull((TContext) context);

        /// <summary>
        /// <para>
        /// This method is called when upstream has signaled that the stream is successfully completed. 
        /// Here you cannot call <see cref="IContext.Push"/>, because there might not be any demand from downstream. 
        /// To emit additional elements before terminating you can use <see cref="IContext.AbsorbTermination"/> and push final elements
        /// from <see cref="OnPull(TContext)"/>. The stage will then be in finishing state, which can be checked
        /// with <see cref="IContext.IsFinishing"/>.
        /// </para>
        /// <para>
        /// By default the finish signal is immediately propagated with <see cref="IContext.Finish"/>.
        /// </para>
        /// <para>
        /// IMPORTANT NOTICE: this signal is not back-pressured, it might arrive from upstream even though
        /// the last action by this stage was a "push".
        /// </para>
        /// </summary>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public sealed override ITerminationDirective OnUpstreamFinish(IContext context) => OnUpstreamFinish((TContext) context);

        /// <summary>
        /// <para>
        /// This method is called when upstream has signaled that the stream is successfully completed. 
        /// Here you cannot call <see cref="IContext.Push"/>, because there might not be any demand from downstream. 
        /// To emit additional elements before terminating you can use <see cref="IContext.AbsorbTermination"/> and push final elements
        /// from <see cref="OnPull(TContext)"/>. The stage will then be in finishing state, which can be checked
        /// with <see cref="IContext.IsFinishing"/>.
        /// </para>
        /// <para>
        /// By default the finish signal is immediately propagated with <see cref="IContext.Finish"/>.
        /// </para>
        /// <para>
        /// IMPORTANT NOTICE: this signal is not back-pressured, it might arrive from upstream even though
        /// the last action by this stage was a "push".
        /// </para>
        /// </summary>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public virtual ITerminationDirective OnUpstreamFinish(TContext context) => context.Finish();

        /// <summary>
        /// This method is called when downstream has cancelled. 
        /// By default the cancel signal is immediately propagated with <see cref="IContext.Finish"/>.
        /// </summary>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public sealed override ITerminationDirective OnDownstreamFinish(IContext context) => OnDownstreamFinish((TContext) context);

        /// <summary>
        /// This method is called when downstream has cancelled. 
        /// By default the cancel signal is immediately propagated with <see cref="IContext.Finish"/>.
        /// </summary>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public virtual ITerminationDirective OnDownstreamFinish(TContext context) => context.Finish();

        /// <summary>
        /// <para>
        /// <see cref="OnUpstreamFailure(System.Exception,Akka.Streams.Stage.IContext)"/> is called when upstream has signaled that the stream is completed
        /// with failure. It is not called if <see cref="OnPull(TContext)"/> or <see cref="OnPush(TIn,TContext)"/> of the stage itself
        /// throws an exception.
        /// </para>
        /// <para>
        /// Note that elements that were emitted by upstream before the failure happened might
        /// not have been received by this stage when <see cref="OnUpstreamFailure(System.Exception,Akka.Streams.Stage.IContext)"/> is called, i.e.
        /// failures are not backpressured and might be propagated as soon as possible.
        /// </para>
        /// <para>
        /// Here you cannot call <see cref="IContext.Push"/>, because there might not
        /// be any demand from  downstream. To emit additional elements before terminating you
        /// can use <see cref="IContext.AbsorbTermination"/> and push final elements
        /// from <see cref="OnPull(TContext)"/>. The stage will then be in finishing state, which can be checked
        /// with <see cref="IContext.IsFinishing"/>.
        /// </para>
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public sealed override ITerminationDirective OnUpstreamFailure(Exception cause, IContext context) => OnUpstreamFailure(cause, (TContext) context);

        /// <summary>
        /// <para>
        /// <see cref="OnUpstreamFailure(System.Exception,Akka.Streams.Stage.IContext)"/> is called when upstream has signaled that the stream is completed
        /// with failure. It is not called if <see cref="OnPull(TContext)"/> or <see cref="OnPush(TIn,TContext)"/> of the stage itself
        /// throws an exception.
        /// </para>
        /// <para>
        /// Note that elements that were emitted by upstream before the failure happened might
        /// not have been received by this stage when <see cref="OnUpstreamFailure(System.Exception,Akka.Streams.Stage.IContext)"/> is called, i.e.
        /// failures are not backpressured and might be propagated as soon as possible.
        /// </para>
        /// <para>
        /// Here you cannot call <see cref="IContext.Push"/>, because there might not
        /// be any demand from  downstream. To emit additional elements before terminating you
        /// can use <see cref="IContext.AbsorbTermination"/> and push final elements
        /// from <see cref="OnPull(TContext)"/>. The stage will then be in finishing state, which can be checked
        /// with <see cref="IContext.IsFinishing"/>.
        /// </para>
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public virtual ITerminationDirective OnUpstreamFailure(Exception cause, TContext context) => context.Fail(cause);
    }
}
