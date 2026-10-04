//-----------------------------------------------------------------------
// <copyright file="Context.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Implementation.Fusing;

namespace Akka.Streams.Stage
{

    //[Flags]
    //public enum Directive
    //{
    //    AsyncDirective = 1,
    //    SyncDirective = 2,
    //    UpstreamDirective = 4 | SyncDirective,
    //    DownstreamDirective = 8 | SyncDirective,
    //    TerminationDirective = 16 | SyncDirective,
    //    // never instantiated
    //    FreeDirective = UpstreamDirective | DownstreamDirective | TerminationDirective | AsyncDirective
    //}

    /// <summary>
    /// Marker interface for directives returned from legacy stream-stage callbacks.
    /// </summary>
    public interface IDirective { }
    /// <summary>
    /// Marker for directives that schedule asynchronous work or notifications.
    /// </summary>
    public interface IAsyncDirective : IDirective { }
    /// <summary>
    /// Marker for directives returned by synchronous stage-context operations.
    /// </summary>
    public interface ISyncDirective : IDirective { }
    /// <summary>
    /// Marker for synchronous directives that affect upstream demand or cancellation.
    /// </summary>
    public interface IUpstreamDirective : ISyncDirective { }
    /// <summary>
    /// Marker for synchronous directives that affect downstream output or completion.
    /// </summary>
    public interface IDownstreamDirective : ISyncDirective { }
    /// <summary>
    /// Marker for synchronous directives that affect stage termination.
    /// </summary>
    public interface ITerminationDirective : ISyncDirective { }
    /// <summary>
    /// Directive type that can represent upstream, downstream, termination, or asynchronous operations.
    /// </summary>
    public sealed class FreeDirective : IUpstreamDirective, IDownstreamDirective, ITerminationDirective, IAsyncDirective { }

    /// <summary>
    /// Provides materialization and attribute information to a legacy stage callback.
    /// </summary>
    public interface ILifecycleContext
    {
        /// <summary>
        /// Returns the Materializer that was used to materialize this Stage/>.
        /// It can be used to materialize sub-flows.
        /// </summary>
        IMaterializer Materializer { get; }

        /// <summary>
        /// Returns operation attributes associated with the this Stage
        /// </summary>
        Attributes Attributes { get; }
    }

    /// <summary>
    /// Passed to the callback methods of <see cref="PushPullStage{TIn,TOut}"/> and <see cref="StatefulStage{TIn,TOut}"/>.
    /// </summary>
    public interface IContext : ILifecycleContext
    {
        /// <summary>
        /// This returns true after <see cref="AbsorbTermination"/> has been used.
        /// </summary>
        bool IsFinishing { get; }

        /// <summary>
        /// Push one element to downstream immediately followed by
        /// cancel of upstreams and complete of downstreams.
        /// </summary>
        /// <param name="element">The final element to send downstream before completing.</param>
        /// <returns>A downstream directive representing the push and finish operation.</returns>
        IDownstreamDirective PushAndFinish(object element);

        /// <summary>
        /// Push one element to downstreams.
        /// </summary>
        /// <param name="element">The element to send downstream.</param>
        /// <returns>A downstream directive representing the push operation.</returns>
        IDownstreamDirective Push(object element);
        
        /// <summary>
        /// Request for more elements from upstreams.
        /// </summary>
        /// <returns>An upstream directive requesting another element.</returns>
        IUpstreamDirective Pull();
        
        /// <summary>
        /// Cancel upstreams and complete downstreams successfully.
        /// </summary>
        /// <returns>A directive representing successful completion and upstream cancellation.</returns>
        FreeDirective Finish();
        
        FreeDirective Finish(Exception cause);

        /// <summary>
        /// Cancel upstreams and complete downstreams with failure.
        /// </summary>
        /// <param name="cause">The exception used to fail the stage and its downstreams.</param>
        /// <returns>A directive representing stage failure.</returns>
        FreeDirective Fail(Exception cause);
        
        /// <summary>
        /// Puts the stage in a finishing state so that
        /// final elements can be pushed from onPull.
        /// </summary>
        /// <returns>A termination directive indicating that termination has been absorbed.</returns>
        ITerminationDirective AbsorbTermination();
    }

    /// <summary>
    /// A context that adds typed output operations to <see cref="IContext"/>.
    /// </summary>
    /// <typeparam name="TOut">The element type emitted downstream.</typeparam>
    public interface IContext<in TOut> : IContext
    {
        /// <summary>
        /// Push one element to downstream immediately followed by
        /// cancel of upstreams and complete of downstreams.
        /// </summary>
        /// <param name="element">The final element to send downstream before completing.</param>
        /// <returns>A downstream directive representing the push and finish operation.</returns>
        IDownstreamDirective PushAndFinish(TOut element);
        
        /// <summary>
        /// Push one element to downstreams.
        /// </summary>
        /// <param name="element">The element to send downstream.</param>
        /// <returns>A downstream directive representing the push operation.</returns>
        IDownstreamDirective Push(TOut element);
    }

    /// <summary>
    /// Passed to the callback methods of <see cref="DetachedStage{TIn,TOut}"/>.
    /// 
    /// <see cref="HoldDownstream"/> and <see cref="HoldUpstream"/> stops execution and at the same time putting the stage in a holding state.
    /// If the stage is in a holding state it contains one absorbed signal, therefore in
    /// this state the only possible command to call is <see cref="PushAndPull"/> which results in two
    /// events making the balance right again: 1 hold + 1 external event = 2 external event
    /// </summary>
    public interface IDetachedContext : IContext
    {
        /// <summary>
        /// This returns true when <see cref="HoldDownstream"/> and <see cref="HoldUpstream"/> has been used
        /// and it is reset to false after <see cref="PushAndPull"/>.
        /// </summary>
        bool IsHoldingBoth { get; }
        /// <summary>
        /// Indicates whether the context is holding an upstream event.
        /// </summary>
        bool IsHoldingUpstream { get; }
        /// <summary>
        /// Indicates whether the context is holding a downstream demand event.
        /// </summary>
        bool IsHoldingDownstream { get; }

        /// <summary>
        /// Sends an element downstream and requests another element upstream, releasing both held events.
        /// </summary>
        /// <param name="element">The element to send downstream.</param>
        /// <returns>A directive representing the push and pull operation.</returns>
        FreeDirective PushAndPull(object element);

        /// <summary>
        /// Holds the current upstream event and pauses until the stage receives an external event.
        /// </summary>
        /// <returns>An upstream directive representing the held upstream event.</returns>
        IUpstreamDirective HoldUpstream();
        /// <summary>
        /// Holds the current upstream event while sending an element downstream.
        /// </summary>
        /// <param name="element">The element to send downstream.</param>
        /// <returns>An upstream directive representing the held event and push.</returns>
        IUpstreamDirective HoldUpstreamAndPush(object element);

        /// <summary>
        /// Holds downstream demand and pauses until the stage receives an external event.
        /// </summary>
        /// <returns>A downstream directive representing the held demand.</returns>
        IDownstreamDirective HoldDownstream();
        /// <summary>
        /// Holds downstream demand while requesting another element upstream.
        /// </summary>
        /// <returns>A downstream directive representing the held demand and pull.</returns>
        IDownstreamDirective HoldDownstreamAndPull();
    }

    /// <summary>
    /// A detached-stage context with typed output operations.
    /// </summary>
    /// <typeparam name="TOut">The element type emitted downstream.</typeparam>
    public interface IDetachedContext<in TOut> : IDetachedContext, IContext<TOut>
    {
        /// <summary>
        /// Sends a typed element downstream and requests another element upstream, releasing both held events.
        /// </summary>
        /// <param name="element">The element to send downstream.</param>
        /// <returns>A directive representing the push and pull operation.</returns>
        FreeDirective PushAndPull(TOut element);
        /// <summary>
        /// Holds the current upstream event while sending a typed element downstream.
        /// </summary>
        /// <param name="element">The element to send downstream.</param>
        /// <returns>An upstream directive representing the held event and push.</returns>
        IUpstreamDirective HoldUpstreamAndPush(TOut element);
    }

    /// <summary>
    /// Callback used to deliver an asynchronous notification to a stage context.
    /// </summary>
    /// <param name="element">The notification delivered to the callback.</param>
    public delegate void AsyncCallback(object element);

    /// <summary>
    /// An asynchronous callback holder that is attached to an <see cref="IAsyncContext{TOut,TExt}"/>.
    /// 
    /// Invoking will eventually lead to <see cref="GraphInterpreter.OnAsyncInput"/>
    /// being called.
    /// 
    /// Dispatch an asynchronous notification. This method is thread-safe and
    /// may be invoked from external execution contexts.
    /// </summary>
    /// <typeparam name="T">The notification type accepted by the callback.</typeparam>
    /// <param name="element">The notification delivered to the callback.</param>
    public delegate void AsyncCallback<in T>(T element);
    
    /// <summary>
    /// This kind of context is available to <see cref="IAsyncContext{TOut,TExt}"/>. It implements the same
    /// interface as for <see cref="IDetachedContext"/> with the addition of being able to obtain
    /// <see cref="AsyncCallback"/> objects that allow the registration of asynchronous notifications.
    /// </summary>
    public interface IAsyncContext : IDetachedContext
    {
        /// <summary>
        /// Obtain a callback object that can be used asynchronously to re-enter the
        /// current <see cref="IAsyncContext{TOut,TExt}"/> with an asynchronous notification. After the
        /// notification has been invoked, eventually <see cref="GraphInterpreter.OnAsyncInput"/>
        /// will be called with the given data item.
        /// 
        /// This object can be cached and reused within the same <see cref="IAsyncContext{TOut,TExt}"/>.
        /// </summary>
        /// <returns>A callback that sends untyped notifications to this asynchronous context.</returns>
        AsyncCallback GetAsyncCallback();

        /// <summary>
        /// In response to an asynchronous notification an <see cref="IAsyncContext{TOut,TExt}"/> may choose
        /// to neither push nor pull nor terminate, which is represented as this directive.
        /// </summary>
        /// <returns>An asynchronous directive that performs no push, pull, or termination.</returns>
        IAsyncDirective Ignore();
        
    }

    /// <summary>
    /// An asynchronous context that also supports typed output and typed external notifications.
    /// </summary>
    /// <typeparam name="TOut">The element type emitted downstream.</typeparam>
    /// <typeparam name="TExt">The external notification type accepted by its callback.</typeparam>
    public interface IAsyncContext<in TOut, in TExt> : IAsyncContext, IDetachedContext<TOut>
    {
        /// <summary>
        /// Obtain a callback object that can be used asynchronously to re-enter the
        /// current <see cref="IAsyncContext{TOut,TExt}"/> with an asynchronous notification. After the
        /// notification has been invoked, eventually <see cref="GraphInterpreter.OnAsyncInput"/>
        /// will be called with the given data item.
        /// 
        /// This object can be cached and reused within the same <see cref="IAsyncContext{TOut,TExt}"/>.
        /// </summary>
        /// <returns>A callback that sends notifications of type <typeparamref name="TExt"/> to this asynchronous context.</returns>
        new AsyncCallback<TExt> GetAsyncCallback();
    }

    /// <summary>
    /// Context exposed to a stage at a boundary between stream regions.
    /// </summary>
    public interface IBoundaryContext : IContext
    {
        /// <summary>
        /// Requests that the boundary context exit its current boundary operation.
        /// </summary>
        /// <returns>A directive representing the exit operation.</returns>
        FreeDirective Exit();
    }
}
