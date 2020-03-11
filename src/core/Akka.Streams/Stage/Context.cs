//-----------------------------------------------------------------------
// <copyright file="Context.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// TBD
    /// </summary>
    public interface IDirective { }
    /// <summary>
    /// TBD
    /// </summary>
    public interface IAsyncDirective : IDirective { }
    /// <summary>
    /// TBD
    /// </summary>
    public interface ISyncDirective : IDirective { }
    /// <summary>
    /// TBD
    /// </summary>
    public interface IUpstreamDirective : ISyncDirective { }
    /// <summary>
    /// TBD
    /// </summary>
    public interface IDownstreamDirective : ISyncDirective { }
    /// <summary>
    /// TBD
    /// </summary>
    public interface ITerminationDirective : ISyncDirective { }
    /// <summary>
    /// TBD
    /// </summary>
    public sealed class FreeDirective : IUpstreamDirective, IDownstreamDirective, ITerminationDirective, IAsyncDirective { }

    /// <summary>
    /// TBD
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
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        IDownstreamDirective PushAndFinish(object element);

        /// <summary>
        /// Push one element to downstreams.
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        IDownstreamDirective Push(object element);
        
        /// <summary>
        /// Request for more elements from upstreams.
        /// </summary>
        /// <returns>TBD</returns>
        IUpstreamDirective Pull();
        
        /// <summary>
        /// Cancel upstreams and complete downstreams successfully.
        /// </summary>
        /// <returns>TBD</returns>
        FreeDirective Finish();
        
        /// <summary>
        /// Cancel upstreams and complete downstreams with failure.
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <returns>TBD</returns>
        FreeDirective Fail(Exception cause);
        
        /// <summary>
        /// Puts the stage in a finishing state so that
        /// final elements can be pushed from onPull.
        /// </summary>
        /// <returns>TBD</returns>
        ITerminationDirective AbsorbTermination();
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TOut">TBD</typeparam>
    public interface IContext<in TOut> : IContext
    {
        /// <summary>
        /// Push one element to downstream immediately followed by
        /// cancel of upstreams and complete of downstreams.
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        IDownstreamDirective PushAndFinish(TOut element);
        
        /// <summary>
        /// Push one element to downstreams.
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        bool IsHoldingUpstream { get; }
        /// <summary>
        /// TBD
        /// </summary>
        bool IsHoldingDownstream { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        FreeDirective PushAndPull(object element);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        IUpstreamDirective HoldUpstream();
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        IUpstreamDirective HoldUpstreamAndPush(object element);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        IDownstreamDirective HoldDownstream();
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        IDownstreamDirective HoldDownstreamAndPull();
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TOut">TBD</typeparam>
    public interface IDetachedContext<in TOut> : IDetachedContext, IContext<TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        FreeDirective PushAndPull(TOut element);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        IUpstreamDirective HoldUpstreamAndPush(TOut element);
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <param name="element">TBD</param>
    public delegate void AsyncCallback(object element);

    /// <summary>
    /// An asynchronous callback holder that is attached to an <see cref="IAsyncContext{TOut,TExt}"/>.
    /// 
    /// Invoking <see cref="Invoke"/> will eventually lead to <see cref="GraphInterpreter.OnAsyncInput"/>
    /// being called.
    /// 
    /// Dispatch an asynchronous notification. This method is thread-safe and
    /// may be invoked from external execution contexts.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <param name="element">TBD</param>
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
        /// <returns>TBD</returns>
        AsyncCallback GetAsyncCallback();

        /// <summary>
        /// In response to an asynchronous notification an <see cref="IAsyncContext{TOut,TExt}"/> may choose
        /// to neither push nor pull nor terminate, which is represented as this directive.
        /// </summary>
        /// <returns>TBD</returns>
        IAsyncDirective Ignore();
        
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TOut">TBD</typeparam>
    /// <typeparam name="TExt">TBD</typeparam>
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
        /// <returns>TBD</returns>
        new AsyncCallback<TExt> GetAsyncCallback();
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IBoundaryContext : IContext
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        FreeDirective Exit();
    }
}
