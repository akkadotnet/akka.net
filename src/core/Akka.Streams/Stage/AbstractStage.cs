//-----------------------------------------------------------------------
// <copyright file="AbstractStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;
using Directive = Akka.Streams.Supervision.Directive;

namespace Akka.Streams.Stage
{
    /// <summary>
    /// Runs a legacy push-pull stage by forwarding graph events to its callback methods.
    /// </summary>
    /// <typeparam name="TIn">The element type received from upstream.</typeparam>
    /// <typeparam name="TOut">The element type emitted downstream.</typeparam>
    internal sealed class PushPullGraphLogic<TIn, TOut> : GraphStageLogic, IDetachedContext<TOut>
    {
#pragma warning disable CS0618 // Type or member is obsolete
        private AbstractStage<TIn, TOut> _currentStage;
#pragma warning restore CS0618 // Type or member is obsolete
        private readonly FlowShape<TIn, TOut> _shape;

        /// <summary>
        /// Creates graph logic for the supplied flow shape, attributes, and legacy stage instance.
        /// </summary>
        /// <param name="shape">The inlet and outlet handled by this logic.</param>
        /// <param name="attributes">The attributes associated with the stage.</param>
        /// <param name="stage">The legacy stage whose callbacks handle graph events.</param>
        public PushPullGraphLogic(
            FlowShape<TIn, TOut> shape,
            Attributes attributes,
#pragma warning disable CS0618 // Type or member is obsolete
            AbstractStage<TIn, TOut> stage)
#pragma warning restore CS0618 // Type or member is obsolete
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
                onDownstreamFinish: cause => _currentStage.OnDownstreamFinish(Context, cause));
        }

        /// <summary>
        /// The legacy stage instance receiving events from this graph logic.
        /// </summary>
#pragma warning disable CS0618 // Type or member is obsolete
        public AbstractStage<TIn, TOut> Stage { get; }
#pragma warning restore CS0618 // Type or member is obsolete

        IMaterializer ILifecycleContext.Materializer => Materializer;

        /// <summary>
        /// The attributes associated with the stage.
        /// </summary>
        public Attributes Attributes { get; }

        /// <summary>
        /// The context passed to the current stage's callbacks.
        /// </summary>
        public IDetachedContext<TOut> Context => this;

        /// <summary>
        /// Pulls once before startup for a detached stage so its first upstream event can be held.
        /// </summary>
        protected internal override void BeforePreStart()
        {
            base.BeforePreStart();
            if (_currentStage.IsDetached)
                Pull(_shape.Inlet);
        }

        /// <summary>
        /// Pushes an element of the stage output type to downstream.
        /// </summary>
        /// <param name="element">The output element to send downstream.</param>
        /// <returns>The legacy context implementation performs the push and returns <see langword="null"/>.</returns>
        public IDownstreamDirective Push(object element) => Push((TOut)element);

        /// <summary>
        /// Pushes an element to downstream.
        /// </summary>
        /// <param name="element">The output element to send downstream.</param>
        /// <returns>The legacy context implementation performs the push and returns <see langword="null"/>.</returns>
        public IDownstreamDirective Push(TOut element)
        {
            Push(_shape.Outlet, element);
            return null;
        }

        /// <summary>
        /// Requests another element from upstream.
        /// </summary>
        /// <returns>The legacy context implementation performs the pull and returns <see langword="null"/>.</returns>
        public IUpstreamDirective Pull()
        {
            Pull(_shape.Inlet);
            return null;
        }

        /// <summary>
        /// Cancels upstream and completes downstream successfully.
        /// </summary>
        /// <returns>The legacy context implementation completes the stage and returns <see langword="null"/>.</returns>
        public FreeDirective Finish()
        {
            return Finish(SubscriptionWithCancelException.NoMoreElementsNeeded.Instance);
        }

        public FreeDirective Finish(Exception cause)
        {
            CancelStage(cause);
            return null;
        }

        /// <summary>
        /// Pushes one final output element and completes the stage.
        /// </summary>
        /// <param name="element">The final output element to send downstream.</param>
        /// <returns>The legacy context implementation pushes and completes the stage, then returns <see langword="null"/>.</returns>
        public IDownstreamDirective PushAndFinish(object element) => PushAndFinish((TOut) element);

        /// <summary>
        /// Pushes one final output element and completes the stage.
        /// </summary>
        /// <param name="element">The final output element to send downstream.</param>
        /// <returns>The legacy context implementation pushes and completes the stage, then returns <see langword="null"/>.</returns>
        public IDownstreamDirective PushAndFinish(TOut element)
        {
            Push(_shape.Outlet, element);
            CompleteStage();
            return null;
        }

        /// <summary>
        /// Fails the stage and its downstream with the supplied exception.
        /// </summary>
        /// <param name="cause">The exception used to fail the stage.</param>
        /// <returns>The legacy context implementation fails the stage and returns <see langword="null"/>.</returns>
        public FreeDirective Fail(Exception cause)
        {
            FailStage(cause);
            return null;
        }

        /// <summary>
        /// Indicates whether upstream has terminated.
        /// </summary>
        public bool IsFinishing => IsClosed(_shape.Inlet);

        /// <summary>
        /// Absorbs upstream termination so final elements can be emitted when downstream demand arrives.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown when the <see cref="FlowShape{TIn,TOut}.Outlet"/> is closed.
        /// </exception>
        /// A termination directive indicating that termination has been absorbed.
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
        /// Pushes an output element and requests another element from upstream.
        /// </summary>
        /// <param name="element">The output element to send downstream.</param>
        /// <returns>The legacy context implementation pushes and pulls, then returns <see langword="null"/>.</returns>
        public FreeDirective PushAndPull(object element) => PushAndPull((TOut) element);

        /// <summary>
        /// Pushes an output element and requests another element from upstream.
        /// </summary>
        /// <param name="element">The output element to send downstream.</param>
        /// <returns>The legacy context implementation pushes and pulls, then returns <see langword="null"/>.</returns>
        public FreeDirective PushAndPull(TOut element)
        {
            Push(_shape.Outlet, element);
            Pull(_shape.Inlet);
            return null;
        }

        /// <summary>
        /// Holds the upstream event while pushing an element downstream.
        /// </summary>
        /// <param name="element">The output element to send downstream.</param>
        /// <returns>The legacy context implementation pushes the element and returns <see langword="null"/>.</returns>
        public IUpstreamDirective HoldUpstreamAndPush(object element) => HoldUpstreamAndPush((TOut) element);

        /// <summary>
        /// Holds the upstream event while pushing an element downstream.
        /// </summary>
        /// <param name="element">The output element to send downstream.</param>
        /// <returns>The legacy context implementation pushes the element and returns <see langword="null"/>.</returns>
        public IUpstreamDirective HoldUpstreamAndPush(TOut element)
        {
            Push(_shape.Outlet, element);
            return null;
        }

        /// <summary>
        /// Holds downstream demand while requesting an element from upstream.
        /// </summary>
        /// <returns>The legacy context implementation pulls upstream and returns <see langword="null"/>.</returns>
        public IDownstreamDirective HoldDownstreamAndPull()
        {
            Pull(_shape.Inlet);
            return null;
        }

        /// <summary>
        /// Indicates whether both an upstream event and downstream demand are being held.
        /// </summary>
        public bool IsHoldingBoth => IsHoldingUpstream && IsHoldingDownstream;

        /// <summary>
        /// Indicates whether downstream demand is being held.
        /// </summary>
        public bool IsHoldingDownstream => IsAvailable(_shape.Outlet);

        /// <summary>
        /// Indicates whether an upstream event is being held.
        /// </summary>
        public bool IsHoldingUpstream => !(IsClosed(_shape.Inlet) || HasBeenPulled(_shape.Inlet));

        /// <summary>
        /// Holds the current downstream demand event.
        /// </summary>
        /// <returns>The legacy context implementation returns <see langword="null"/>.</returns>
        public IDownstreamDirective HoldDownstream() => null;

        /// <summary>
        /// Holds the current upstream event.
        /// </summary>
        /// <returns>The legacy context implementation returns <see langword="null"/>.</returns>
        public IUpstreamDirective HoldUpstream() => null;

        /// <summary>
        /// Invokes the stage's <see cref="AbstractStage{TIn,TOut}.PreStart"/> callback.
        /// </summary>
        public override void PreStart() => _currentStage.PreStart(Context);

        /// <summary>
        /// Invokes the stage's <see cref="AbstractStage{TIn,TOut}.PostStop"/> callback.
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
#pragma warning disable CS0618 // Type or member is obsolete
                    _currentStage = (AbstractStage<TIn, TOut>)_currentStage.Restart();
#pragma warning restore CS0618 // Type or member is obsolete
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
        /// Returns a string identifying this graph logic and its current stage.
        /// </summary>
        /// <returns>The logic name with the current stage representation.</returns>
        public override string ToString() => $"PushPullGraphLogic({_currentStage})";
    }

    /// <summary>
    /// Adapts a legacy stage factory to a graph stage with a materialized value.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the inlet.</typeparam>
    /// <typeparam name="TOut">The element type emitted by the outlet.</typeparam>
    /// <typeparam name="TMat">The type of the materialized value produced by the factory.</typeparam>
    public class PushPullGraphStageWithMaterializedValue<TIn, TOut, TMat> : GraphStageWithMaterializedValue<FlowShape<TIn, TOut>, TMat>
    {
        /// <summary>
        /// A factory that creates a legacy stage and its materialized value from effective attributes.
        /// </summary>
#pragma warning disable CS0618 // Type or member is obsolete
        public readonly Func<Attributes, (IStage<TIn, TOut>, TMat)> Factory;
#pragma warning restore CS0618 // Type or member is obsolete

        /// <summary>
        /// Creates a graph stage from a factory and the stage's initial attributes.
        /// </summary>
        /// <param name="factory">Creates a legacy stage and materialized value for a set of attributes.</param>
        /// <param name="stageAttributes">The initial attributes applied to this graph stage.</param>
#pragma warning disable CS0618 // Type or member is obsolete
        public PushPullGraphStageWithMaterializedValue(Func<Attributes, (IStage<TIn, TOut>, TMat)> factory, Attributes stageAttributes)
#pragma warning restore CS0618 // Type or member is obsolete
        {
            InitialAttributes = stageAttributes;
            Factory = factory;

            var name = stageAttributes.GetNameOrDefault();
            Shape = new FlowShape<TIn, TOut>(new Inlet<TIn>(name + ".in"), new Outlet<TOut>(name + ".out"));
        }

        /// <summary>
        /// The initial attributes applied to this stage's module.
        /// </summary>
        protected override Attributes InitialAttributes { get; }

        /// <summary>
        /// The flow shape containing this stage's inlet and outlet.
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// Creates legacy stage logic and its materialized value using the effective attributes.
        /// </summary>
        /// <param name="inheritedAttributes">The attributes effective for this stage materialization.</param>
        /// <returns>A graph logic wrapper for the legacy stage returned by the factory, together with the factory's materialized value.</returns>
        public override ILogicAndMaterializedValue<TMat> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var stageAndMat = Factory(inheritedAttributes);
#pragma warning disable CS0618 // Type or member is obsolete
            return
                new LogicAndMaterializedValue<TMat>(
                    new PushPullGraphLogic<TIn, TOut>(Shape, inheritedAttributes,
                        (AbstractStage<TIn, TOut>) stageAndMat.Item1), stageAndMat.Item2);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        /// <summary>
        /// Returns the name derived from this stage's initial attributes.
        /// </summary>
        /// <returns>The stage name, or the default name when no name attribute is present.</returns>
        public sealed override string ToString() => InitialAttributes.GetNameOrDefault();
    }

    /// <summary>
    /// Adapts a legacy stage factory that produces no materialized value to a graph stage.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the inlet.</typeparam>
    /// <typeparam name="TOut">The element type emitted by the outlet.</typeparam>
    public class PushPullGraphStage<TIn, TOut> : PushPullGraphStageWithMaterializedValue<TIn, TOut, NotUsed>
    {
        /// <summary>
        /// Creates a graph stage from a factory that produces a legacy stage.
        /// </summary>
        /// <param name="factory">Creates a legacy stage for the effective attributes.</param>
        /// <param name="stageAttributes">The initial attributes applied to this graph stage.</param>
        /// <returns>A graph stage whose materialized value is <see cref="NotUsed"/>.</returns>
#pragma warning disable CS0618 // Type or member is obsolete
        public PushPullGraphStage(Func<Attributes, IStage<TIn, TOut>> factory, Attributes stageAttributes) : base(attributes => (factory(attributes), NotUsed.Instance), stageAttributes)
#pragma warning restore CS0618 // Type or member is obsolete
        {
        }
    }

    /// <summary>
    /// Base class for a legacy stream stage that processes input and output elements through callbacks.
    /// </summary>
    /// <typeparam name="TIn">The element type received from upstream.</typeparam>
    /// <typeparam name="TOut">The element type emitted downstream.</typeparam>
    [Obsolete("Please use GraphStage instead. [1.1.2]")]
    public abstract class AbstractStage<TIn, TOut> : IStage<TIn, TOut>
    {
        /// <summary>
        /// Indicates whether this stage separates adjacent one-bounded regions.
        /// </summary>
        protected internal virtual bool IsDetached => false;
        
        /// <summary>
        /// User overridable callback.
        /// <para>
        /// It is called before any other method defined on the <see cref="IStage{TIn,TOut}"/>.
        /// Empty default implementation.
        /// </para>
        /// </summary>
        /// <param name="context">The lifecycle context for this stage.</param>
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
        /// <param name="element">The element received from upstream.</param>
        /// <param name="context">The context used to push, pull, or terminate during this callback.</param>
        /// <returns>The directive describing the operation performed.</returns>
        public abstract IDirective OnPush(TIn element, IContext context);

        /// <summary>
        /// This method is called when there is demand from downstream, i.e. you are allowed to push one element
        /// downstreams with <see cref="IContext.Push"/>, or request elements from upstreams with <see cref="IContext.Pull"/>
        /// </summary>
        /// <param name="context">The context used to push, pull, or terminate during this callback.</param>
        /// <returns>The directive describing the operation performed.</returns>
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
        /// By default the finish signal is immediately propagated with <see cref="StatefulStage.Finish"/>.
        /// </para>
        /// <para>
        /// IMPORTANT NOTICE: this signal is not back-pressured, it might arrive from upstream even though
        /// the last action by this stage was a "push".
        /// </para>
        /// </summary>
        /// <param name="context">The context used to finish or absorb upstream completion.</param>
        /// <returns>The termination directive describing how completion is handled.</returns>
        public abstract ITerminationDirective OnUpstreamFinish(IContext context);

        /// <summary>
        /// This method is called when downstream has cancelled. 
        /// By default the cancel signal is immediately propagated with <see cref="StatefulStage.Finish"/>.
        /// </summary>
        /// <param name="context">The context used to handle downstream cancellation.</param>
        /// <param name="cause">The cancellation cause provided by downstream.</param>
        /// <returns>The termination directive describing how cancellation is handled.</returns>
        public abstract ITerminationDirective OnDownstreamFinish(IContext context, Exception cause);

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
        /// <param name="cause">The exception signaled by upstream.</param>
        /// <param name="context">The context used to absorb the failure or terminate the stage.</param>
        /// <returns>The termination directive describing how the failure is handled.</returns>
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
        /// <param name="cause">The exception thrown while processing an element.</param>
        /// <returns>The supervision directive selected for the exception.</returns>
        public virtual Directive Decide(Exception cause) => Directive.Stop;

        /// <summary>
        /// Used to create a fresh instance of the stage after an error resulting in a <see cref="Directive.Restart"/>
        /// directive. By default it will return the same instance untouched, so you must override it
        /// if there are any state that should be cleared before restarting, e.g. by returning a new instance.
        /// </summary>
        /// <returns>The stage instance used after a restart directive; the default returns this instance.</returns>
        public virtual IStage<TIn, TOut> Restart() => this;
    }

    /// <summary>
    /// Base class for legacy stages with typed contexts and separate directive types for push and pull callbacks.
    /// </summary>
    /// <typeparam name="TIn">The element type received from upstream.</typeparam>
    /// <typeparam name="TOut">The element type emitted downstream.</typeparam>
    /// <typeparam name="TPushDirective">The directive type returned by push callbacks.</typeparam>
    /// <typeparam name="TPullDirective">The directive type returned by pull callbacks.</typeparam>
    /// <typeparam name="TContext">The context type provided to the stage callbacks.</typeparam>
    [Obsolete("Please use GraphStage instead. [1.1.2]")]
    public abstract class AbstractStage<TIn, TOut, TPushDirective, TPullDirective, TContext> : AbstractStage<TIn, TOut> where TPushDirective : IDirective where TPullDirective : IDirective where TContext : IContext
    {
        /// <summary>
        /// The context used for the typed stage callbacks.
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
        /// <param name="element">The element received from upstream.</param>
        /// <param name="context">The typed context used by this callback.</param>
        /// <returns>The push directive describing the operation performed.</returns>
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
        /// <param name="element">The element received from upstream.</param>
        /// <param name="context">The untyped context forwarded to the typed callback.</param>
        /// <returns>The directive returned by the typed push callback.</returns>
        public sealed override IDirective OnPush(TIn element, IContext context) => OnPush(element, (TContext) context);

        /// <summary>
        /// This method is called when there is demand from downstream, i.e. you are allowed to push one element
        /// downstreams with <see cref="IContext.Push"/>, or request elements from upstreams with <see cref="IContext.Pull"/>
        /// </summary>
        /// <param name="context">The typed context used by this callback.</param>
        /// <returns>The pull directive describing the operation performed.</returns>
        public abstract TPullDirective OnPull(TContext context);


        /// <summary>
        /// This method is called when there is demand from downstream, i.e. you are allowed to push one element
        /// downstreams with <see cref="IContext.Push"/>, or request elements from upstreams with <see cref="IContext.Pull"/>
        /// </summary>
        /// <param name="context">The untyped context forwarded to the typed callback.</param>
        /// <returns>The directive returned by the typed pull callback.</returns>
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
        /// By default the finish signal is immediately propagated with <see cref="StatefulStage.Finish"/>.
        /// </para>
        /// <para>
        /// IMPORTANT NOTICE: this signal is not back-pressured, it might arrive from upstream even though
        /// the last action by this stage was a "push".
        /// </para>
        /// </summary>
        /// <param name="context">The untyped context forwarded to the typed callback.</param>
        /// <returns>The termination directive returned by the typed completion callback.</returns>
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
        /// By default the finish signal is immediately propagated with <see cref="StatefulStage.Finish"/>.
        /// </para>
        /// <para>
        /// IMPORTANT NOTICE: this signal is not back-pressured, it might arrive from upstream even though
        /// the last action by this stage was a "push".
        /// </para>
        /// </summary>
        /// <param name="context">The typed context used to handle upstream completion.</param>
        /// <returns>The termination directive describing how completion is handled.</returns>
        public virtual ITerminationDirective OnUpstreamFinish(TContext context) => context.Finish();

        /// <summary>
        /// This method is called when downstream has cancelled. 
        /// By default the cancel signal is immediately propagated with <see cref="StatefulStage.Finish"/>.
        /// </summary>
        /// <param name="context">The untyped context forwarded to the typed callback.</param>
        /// <param name="cause">The cancellation cause provided by downstream.</param>
        /// <returns>The termination directive returned by the typed cancellation callback.</returns>
        public sealed override ITerminationDirective OnDownstreamFinish(IContext context, Exception cause) => OnDownstreamFinish((TContext) context, cause);

        /// <summary>
        /// This method is called when downstream has cancelled. 
        /// By default the cancel signal is immediately propagated with <see cref="StatefulStage.Finish"/>.
        /// </summary>
        /// <param name="context">The typed context used to handle downstream cancellation.</param>
        /// <param name="cause">The cancellation cause provided by downstream.</param>
        /// <returns>The termination directive describing how cancellation is handled.</returns>
        public virtual ITerminationDirective OnDownstreamFinish(TContext context, Exception cause) => context.Finish(cause);

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
        /// <param name="cause">The exception signaled by upstream.</param>
        /// <param name="context">The untyped context forwarded to the typed callback.</param>
        /// <returns>The termination directive returned by the typed failure callback.</returns>
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
        /// <param name="cause">The exception signaled by upstream.</param>
        /// <param name="context">The typed context used to handle the failure.</param>
        /// <returns>The termination directive describing how the failure is handled.</returns>
        public virtual ITerminationDirective OnUpstreamFailure(Exception cause, TContext context) => context.Fail(cause);
    }
}
