//-----------------------------------------------------------------------
// <copyright file="Stage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;

namespace Akka.Streams.Stage
{
    /// <summary>
    /// General interface for stream transformation.
    /// 
    /// Custom <see cref="IStage{TIn, TOut}"/> implementations are intended to be used with
    /// <see cref="FlowOperations.Transform{TIn,TOut1,TOut2,TMat}"/> to extend the <see cref="FlowOperations"/> API when there
    /// is no specialized operator that performs the transformation.
    /// 
    /// Custom implementations are subclasses of <see cref="PushPullStage{TIn, TOut}"/> or
    /// <see cref="DetachedStage{TIn, TOut}"/>. Sometimes it is convenient to extend
    /// <see cref="StatefulStage{TIn, TOut}"/> for support of become like behavior.
    /// 
    /// It is possible to keep state in the concrete <see cref="IStage{TIn, TOut}"/> instance with
    /// ordinary instance variables. The <see cref="ITransformerLike{TIn,TOut}"/> is executed by an actor and
    /// therefore you do not have to add any additional thread safety or memory
    /// visibility constructs to access the state from the callback methods.
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [Obsolete("Please use GraphStage instead. [1.1.0]")]
    public interface IStage<in TIn, out TOut> { }

    /// <summary>
    /// <para>
    /// <see cref="PushPullStage{TIn,TOut}"/> implementations participate in 1-bounded regions. For every external non-completion signal these
    /// stages produce *exactly one* push or pull signal.
    /// </para>
    /// <para>
    /// <see cref="AbstractStage{TIn,TOut}.OnPush"/> is called when an element from upstream is available and there is demand from downstream, i.e.
    /// in <see cref="AbstractStage{TIn,TOut}.OnPush"/> you are allowed to call <see cref="IContext.Push"/> to emit one element downstream, or you can absorb the
    /// element by calling <see cref="IContext.Pull"/>. Note that you can only emit zero or one element downstream from <see cref="AbstractStage{TIn,TOut}.OnPull"/>.
    /// To emit more than one element you have to push the remaining elements from <see cref="AbstractStage{TIn,TOut}.OnPush"/>, one-by-one.
    /// <see cref="AbstractStage{TIn,TOut}.OnPush"/> is not called again until <see cref="AbstractStage{TIn,TOut}.OnPull"/> has requested more elements with <see cref="IContext.Pull"/>.
    /// </para>
    /// <para>
    /// <see cref="StatefulStage{TIn,TOut}"/> has support for making it easy to emit more than one element from <see cref="AbstractStage{TIn,TOut}.OnPush"/>.
    /// </para>
    /// <para>
    /// <see cref="AbstractStage{TIn,TOut}.OnPull"/>> is called when there is demand from downstream, i.e. you are allowed to push one element
    /// downstream with <see cref="IContext.Push"/>, or request elements from upstreams with <see cref="IContext.Pull"/>. If you
    /// always perform transitive pull by calling <see cref="IContext.Pull"/> from <see cref="AbstractStage{TIn,TOut}.OnPull"/> you can use 
    /// <see cref="PushStage{TIn,TOut}"/> instead of <see cref="PushPullStage{TIn,TOut}"/>.
    /// </para>
    /// <para>
    /// Stages are allowed to do early completion of downstream and cancel of upstream. This is done with <see cref="IContext.Finish"/>,
    /// which is a combination of cancel/complete.
    /// </para>
    /// <para>
    /// Since OnComplete is not a backpressured signal it is sometimes preferable to push a final element and then
    /// immediately finish. This combination is exposed as <see cref="IContext.PushAndFinish"/> which enables stages to
    /// propagate completion events without waiting for an extra round of pull.
    /// </para>
    /// <para>
    /// Another peculiarity is how to convert termination events (complete/failure) into elements. The problem
    /// here is that the termination events are not backpressured while elements are. This means that simply calling
    /// <see cref="IContext.Push"/> as a response to <see cref="AbstractStage{TIn,TOut}.OnUpstreamFinish(IContext)"/> or <see cref="AbstractStage{TIn,TOut}.OnUpstreamFailure(Exception,IContext)"/> will very likely break boundedness
    /// and result in a buffer overflow somewhere. Therefore the only allowed command in this case is
    /// <see cref="IContext.AbsorbTermination"/> which stops the propagation of the termination signal, and puts the stage in a
    /// <see cref="IContext.IsFinishing"/> state. Depending on whether the stage has a pending pull signal it
    /// has not yet "consumed" by a push its <see cref="AbstractStage{TIn,TOut}.OnPull"/> handler might be called immediately or later. From
    /// <see cref="AbstractStage{TIn,TOut}.OnPull"/> final elements can be pushed before completing downstream with <see cref="IContext.Finish"/> or
    /// <see cref="IContext.PushAndFinish"/>.
    /// </para>
    /// <para>
    /// <see cref="StatefulStage{TIn,TOut}"/> has support for making it easy to emit final elements.
    /// </para>
    /// <para>
    /// All these rules are enforced by types and runtime checks where needed. Always return the <see cref="Directive"/>
    /// from the call to the <see cref="IContext"/> method, and do only call <see cref="IContext"/> commands once per callback.
    /// </para>
    /// </summary>
    /// <seealso cref="DetachedStage{TIn,TOut}"/>
    /// <seealso cref="StatefulStage{TIn,TOut}"/>
    /// <seealso cref="PushStage{TIn,TOut}"/>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [Obsolete("Please use GraphStage instead. [1.1.0]")]
    public abstract class PushPullStage<TIn, TOut> : AbstractStage<TIn, TOut, ISyncDirective, ISyncDirective, IContext<TOut>> { }

    /// <summary>
    /// <see cref="PushStage{TIn,TOut}"/> is a <see cref="PushPullStage{TIn,TOut}"/> that always perform transitive pull by calling <see cref="IContext.Pull"/> from <see cref="OnPull"/>.
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [Obsolete("Please use GraphStage instead. [1.1.0]")]
    public abstract class PushStage<TIn, TOut> : PushPullStage<TIn, TOut>
    {
        /// <summary>
        /// Always pulls from upstream.
        /// </summary>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public sealed override ISyncDirective OnPull(IContext<TOut> context) => context.Pull();
    }

    /// <summary>
    /// DetachedStage can be used to implement operations similar to <see cref="FlowOperations.Buffer{TIn,TOut,TMat}"/>,
    /// <see cref="FlowOperations.Expand{TIn,TOut1,TOut2,TMat}"/> and <see cref="FlowOperations.Conflate{TIn,TOut,TMat}"/>.
    /// 
    /// DetachedStage implementations are boundaries between 1-bounded regions. This means that they need to enforce the
    /// "exactly one" property both on their upstream and downstream regions. As a consequence a DetachedStage can never
    /// answer an <see cref="AbstractStage{TIn,TOut}.OnPull"/> with a <see cref="IContext.Pull"/> or answer an <see cref="AbstractStage{TIn,TOut}.OnPush"/> with a <see cref="IContext.Push"/> since such an action
    /// would "steal" the event from one region (resulting in zero signals) and would inject it to the other region
    /// (resulting in two signals).
    /// 
    /// However, DetachedStages have the ability to call <see cref="IDetachedContext.HoldUpstream"/> and <see cref="IDetachedContext.HoldDownstream"/> as a response to
    /// <see cref="AbstractStage{TIn,TOut}.OnPush"/> and <see cref="AbstractStage{TIn,TOut}.OnPull"/> which temporarily takes the signal off and
    /// stops execution, at the same time putting the stage in an <see cref="IDetachedContext.IsHoldingBoth"/> state.
    /// If the stage is in a holding state it contains one absorbed signal, therefore in this state the only possible
    /// command to call is <see cref="IDetachedContext.PushAndPull"/> which results in two events making the
    /// balance right again: 1 hold + 1 external event = 2 external event
    /// 
    /// This mechanism allows synchronization between the upstream and downstream regions which otherwise can progress
    /// independently.
    /// 
    /// @see <see cref="PushPullStage{TIn,TOut}"/>
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [Obsolete("Please use GraphStage instead. [1.1.0]")]
    public abstract class DetachedStage<TIn, TOut> : AbstractStage<TIn, TOut, IUpstreamDirective, IDownstreamDirective, IDetachedContext<TOut>>
    {
        /// <summary>
        /// TBD
        /// </summary>
        protected internal override bool IsDetached => true;
    }

    /// <summary>
    /// The behavior of <see cref="StatefulStage{TIn,TOut}"/> is defined by these two methods, which
    /// has the same semantics as corresponding methods in <see cref="PushPullStage{TIn,TOut}"/>.
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public abstract class StageState<TIn, TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public abstract ISyncDirective OnPush(TIn element, IContext<TOut> context);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public virtual ISyncDirective OnPull(IContext<TOut> context) => context.Pull();
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class StatefulStage
    {
        #region Internal API

        /// <summary>
        /// TBD
        /// </summary>
        internal interface IAndThen { }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        internal sealed class Finish : IAndThen
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Finish Instance = new Finish();
            private Finish() { }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        [Serializable]
        internal sealed class Become<TIn, TOut> : IAndThen
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly StageState<TIn, TOut> State;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="state">TBD</param>
            public Become(StageState<TIn, TOut> state)
            {
                State = state;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        internal sealed class Stay : IAndThen
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Stay Instance = new Stay();
            private Stay() { }
        }

        #endregion
    }

    /// <summary>
    /// <see cref="StatefulStage{TIn,TOut}"/> is a <see cref="PushPullStage{TIn,TOut}"/> that provides convenience to make some things easier.
    /// 
    /// The behavior is defined in <see cref="StageState{TIn,TOut}"/> instances. The initial behavior is specified
    /// by subclass implementing the <see cref="Initial"/> method. The behavior can be changed by using <see cref="Become"/>.
    /// 
    /// Use <see cref="Emit(IEnumerator{TOut},IContext{TOut},StageState{TIn,TOut})"/> or <see cref="EmitAndFinish"/> to push more than one element from <see cref="StageState{TIn,TOut}.OnPush"/> or
    /// <see cref="StageState{TIn,TOut}.OnPull"/>.
    /// 
    /// Use <see cref="TerminationEmit"/> to push final elements from <see cref="OnUpstreamFinish"/> or <see cref="AbstractStage{TIn,TOut}.OnUpstreamFailure"/>.
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [Obsolete("Please use GraphStage instead. [1.1.0]")]
    public abstract class StatefulStage<TIn, TOut> : PushPullStage<TIn, TOut>
    {
        private bool _isEmitting;
        private StageState<TIn, TOut> _current;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="current">TBD</param>
        protected StatefulStage(StageState<TIn, TOut> current)
        {
            _current = current;
            Become(Initial);
        }

        /// <summary>
        /// Concrete subclass must return the initial behavior from this method.
        /// **Warning:** This method must not be implemented as `val`.
        /// </summary>
        public abstract StageState<TIn, TOut> Initial { get; }

        /// <summary>
        /// Current state.
        /// </summary>
        public StageState<TIn, TOut> Current => _current;

        /// <summary>
        /// Change the behavior to another <see cref="StageState{TIn,TOut}"/>.
        /// </summary>
        /// <param name="state">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="state"/> is undefined.
        /// </exception>
        public void Become(StageState<TIn, TOut> state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            _current = state;
        }

        /// <summary>
        /// Invokes current state.
        /// </summary>
        /// <param name="element">TBD</param>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public sealed override ISyncDirective OnPush(TIn element, IContext<TOut> context) => _current.OnPush(element, context);

        /// <summary>
        /// Invokes current state.
        /// </summary>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public sealed override ISyncDirective OnPull(IContext<TOut> context) => _current.OnPull(context);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public override ITerminationDirective OnUpstreamFinish(IContext<TOut> context)
        {
            return _isEmitting
                ? context.AbsorbTermination()
                : context.Finish();
        }

        /// <summary>
        /// Can be used from <see cref="StageState{TIn,TOut}.OnPush"/> or <see cref="StageState{TIn,TOut}.OnPull"/> to push more than one
        /// element downstream.
        /// </summary>
        /// <param name="enumerator">TBD</param>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public ISyncDirective Emit(IEnumerator<TOut> enumerator, IContext<TOut> context) => Emit(enumerator, context, _current);

        /// <summary>
        /// Can be used from <see cref="StageState{TIn,TOut}.OnPush"/> or <see cref="StageState{TIn,TOut}.OnPull"/> to push more than one
        /// element downstream and after that change behavior.
        /// </summary>
        /// <param name="enumerator">TBD</param>
        /// <param name="context">TBD</param>
        /// <param name="nextState">TBD</param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when either currently in the emitting state or the specified <paramref name="enumerator"/> is empty.
        /// </exception>
        /// <returns>TBD</returns>
        public ISyncDirective Emit(IEnumerator<TOut> enumerator, IContext<TOut> context, StageState<TIn, TOut> nextState)
        {
            if (_isEmitting) throw new IllegalStateException("Already in emitting state");
            if (!enumerator.MoveNext())
            {
                Become(nextState);
                return context.Pull();
            }

            var element = enumerator.Current;
            if (enumerator.MoveNext())
            {
                _isEmitting = true;
                Become(EmittingState(enumerator, new StatefulStage.Become<TIn, TOut>(nextState)));
            }
            else
                Become(nextState);

            return context.Push(element);
        }

        /// <summary>
        /// Can be used from <see cref="OnUpstreamFinish"/> to push final elements downstream
        /// before completing the stream successfully. Note that if this is used from
        /// <see cref="AbstractStage{TIn,TOut}.OnUpstreamFailure"/> the failure will be absorbed and the stream will be completed
        /// successfully.
        /// </summary>
        /// <param name="enumerator">TBD</param>
        /// <param name="context">TBD</param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the specified <paramref name="enumerator"/> is empty.
        /// </exception>
        /// <returns>TBD</returns>
        public ISyncDirective TerminationEmit(IEnumerator<TOut> enumerator, IContext<TOut> context)
        {
            if (!enumerator.MoveNext())
                return _isEmitting ? context.AbsorbTermination() : context.Finish();

            var es = Current as EmittingState<TIn, TOut>;
            var nextState = es != null && _isEmitting
                ? es.Copy(enumerator)
                : EmittingState(enumerator, StatefulStage.Finish.Instance);
            Become(nextState);
            return context.AbsorbTermination();
        }

        /// <summary>
        /// Can be used from <see cref="StageState{TIn,TOut}.OnPush"/> or <see cref="StageState{TIn,TOut}.OnPull"/> to push more than one
        /// element downstream and after that finish (complete downstream, cancel upstreams).
        /// </summary>
        /// <param name="enumerator">TBD</param>
        /// <param name="context">TBD</param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when either currently in the emitting state or the specified <paramref name="enumerator"/> is empty.
        /// </exception>
        /// <returns>TBD</returns>
        public ISyncDirective EmitAndFinish(IEnumerator<TOut> enumerator, IContext<TOut> context)
        {
            if(_isEmitting)
                throw new IllegalStateException("Already emitting a state");
            if (!enumerator.MoveNext())
                return context.Finish();

            var elem = enumerator.Current;
            if (enumerator.MoveNext())
            {
                _isEmitting = true;
                Become(EmittingState(enumerator, StatefulStage.Finish.Instance));
                return context.Push(elem);
            }

            return context.PushAndFinish(elem);
        }

        private StageState<TIn, TOut> EmittingState(IEnumerator<TOut> enumerator, StatefulStage.IAndThen andThen)
        {
            return new EmittingState<TIn, TOut>(enumerator, andThen, context =>
            {
                if (enumerator.MoveNext())
                {
                    var element = enumerator.Current;
                    if (enumerator.MoveNext())
                        return context.Push(element);

                    if (!context.IsFinishing)
                    {
                        _isEmitting = false;

                        if (andThen is StatefulStage.Stay) ;
                        else if (andThen is StatefulStage.Become<TIn, TOut>)
                        {
                            var become = andThen as StatefulStage.Become<TIn, TOut>;
                            Become(become.State);
                        }
                        else if (andThen is StatefulStage.Finish)
                            context.PushAndFinish(element);

                        return context.Push(element);
                    }

                    return context.PushAndFinish(element);
                }

                throw new IllegalStateException("OnPull with empty enumerator is not expected in emitting state");
            });
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    internal sealed class EmittingState<TIn, TOut> : StageState<TIn, TOut>
    {
        private readonly IEnumerator<TOut> _enumerator;
        private readonly Func<IContext<TOut>, ISyncDirective> _onPull;
        private readonly StatefulStage.IAndThen _andThen;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="enumerator">TBD</param>
        /// <param name="andThen">TBD</param>
        /// <param name="onPull">TBD</param>
        public EmittingState(IEnumerator<TOut> enumerator, StatefulStage.IAndThen andThen, Func<IContext<TOut>, ISyncDirective> onPull)
        {
            _enumerator = enumerator;
            _onPull = onPull;
            _andThen = andThen;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <exception cref="NotImplementedException">TBD</exception>
        /// <returns>TBD</returns>
        public override ISyncDirective OnPull(IContext<TOut> context)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <param name="context">TBD</param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when currently in the emitting state.
        /// </exception>
        /// <returns>TBD</returns>
        public override ISyncDirective OnPush(TIn element, IContext<TOut> context)
        {
            throw new IllegalStateException("OnPush is not allowed in emitting state");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="enumerator">TBD</param>
        /// <exception cref="NotImplementedException">TBD</exception>
        /// <returns>TBD</returns>
        public StageState<TIn, TOut> Copy(IEnumerator<TOut> enumerator)
        {
            throw new NotImplementedException();
        }
    }
}
