using System;

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

    public interface IDirective { }
    public interface IAsyncDirective : IDirective { }
    public interface ISyncDirective : IDirective { }
    public interface IUpstreamDirective : ISyncDirective { }
    public interface IDownstreamDirective : ISyncDirective { }
    public interface ITerminationDirective : ISyncDirective { }
    public sealed class FreeDirective : IUpstreamDirective, IDownstreamDirective, ITerminationDirective, IAsyncDirective { }
    
    public interface ILifecycleContext
    {
        /**
         * Returns the Materializer that was used to materialize this [[Stage]].
         * It can be used to materialize sub-flows.
         */
        IMaterializer Materializer { get; }

        /** Returns operation attributes associated with the this Stage */
        Attributes Attributes { get; }
    }

    /**
     * Passed to the callback methods of [[PushPullStage]] and [[StatefulStage]].
     */
    public interface IContext : ILifecycleContext
    {
        /**
         * This returns `true` after [[#absorbTermination]] has been used.
         */
        bool IsFinishing { get; }

        void Enter();
        void Execute();

        /**
         * Push one element to downstream immediately followed by
         * cancel of upstreams and complete of downstreams.
         */
        IDownstreamDirective PushAndFinish(object element);

        /**
         * Push one element to downstreams.
         */
        IDownstreamDirective Push(object element);

        /**
         * Request for more elements from upstreams.
         */
        IUpstreamDirective Pull();

        /**
         * Cancel upstreams and complete downstreams successfully.
         */
        FreeDirective Finish();
        
        /**
         * Cancel upstreams and complete downstreams with failure.
         */
        FreeDirective Fail(Exception cause);

        /**
         * Puts the stage in a finishing state so that
         * final elements can be pushed from `onPull`.
         */
        ITerminationDirective AbsorbTermination();
    }

    public interface IContext<in TOut> : IContext
    {
        /**
         * Push one element to downstream immediately followed by
         * cancel of upstreams and complete of downstreams.
         */
        IDownstreamDirective PushAndFinish(TOut element);

        /**
         * Push one element to downstreams.
         */
        IDownstreamDirective Push(TOut element);
    }

    /**
     * Passed to the callback methods of [[DetachedStage]].
     *
     * [[#hold]] stops execution and at the same time putting the stage in a holding state.
     * If the stage is in a holding state it contains one absorbed signal, therefore in
     * this state the only possible command to call is [[#pushAndPull]] which results in two
     * events making the balance right again: 1 hold + 1 external event = 2 external event
     */

    public interface IDetachedContext : IContext
    {
        /**
         * This returns `true` when [[#hold]] has been used
         * and it is reset to `false` after [[#pushAndPull]].
         */
        bool IsHoldingBoth { get; }
        bool IsHoldingUpstream { get; }
        bool IsHoldingDownstream { get; }

        FreeDirective PushAndPull(object element);

        IUpstreamDirective HoldUpstream();
        IDownstreamDirective HoldUpstreamAndPush(object element);

        IDownstreamDirective HoldDownstream();
        IDownstreamDirective HoldDownstreamAndPull();
        
    }
    public interface IDetachedContext<in TOut> : IDetachedContext, IContext<TOut>
    {
        FreeDirective PushAndPull(TOut element);
        IDownstreamDirective HoldUpstreamAndPush(TOut element);
    }

    public delegate void AsyncCallback(object element);

    /**
     * An asynchronous callback holder that is attached to an [[AsyncContext]].
     * Invoking [[AsyncCallback#invoke]] will eventually lead to [[AsyncStage#onAsyncInput]]
     * being called.
     * 
     * Dispatch an asynchronous notification. This method is thread-safe and
     * may be invoked from external execution contexts.
     */
    public delegate void AsyncCallback<in T>(T element);

    /**
     * This kind of context is available to [[AsyncStage]]. It implements the same
     * interface as for [[DetachedStage]] with the addition of being able to obtain
     * [[AsyncCallback]] objects that allow the registration of asynchronous
     * notifications.
     */
    public interface IAsyncContext : IDetachedContext
    {
        /**
         * Obtain a callback object that can be used asynchronously to re-enter the
         * current [[AsyncStage]] with an asynchronous notification. After the
         * notification has been invoked, eventually [[AsyncStage#onAsyncInput]]
         * will be called with the given data item.
         *
         * This object can be cached and reused within the same [[AsyncStage]].
         */
        AsyncCallback GetAsyncCallback();

        /**
         * In response to an asynchronous notification an [[AsyncStage]] may choose
         * to neither push nor pull nor terminate, which is represented as this
         * directive.
         */
        IAsyncDirective Ignore();
        
    }

    public interface IAsyncContext<in TOut, in TExt> : IAsyncContext, IDetachedContext<TOut>
    {
        /**
         * Obtain a callback object that can be used asynchronously to re-enter the
         * current [[AsyncStage]] with an asynchronous notification. After the
         * notification has been invoked, eventually [[AsyncStage#onAsyncInput]]
         * will be called with the given data item.
         *
         * This object can be cached and reused within the same [[AsyncStage]].
         */
        new AsyncCallback<TExt> GetAsyncCallback();
    }

    public interface IBoundaryContext : IContext
    {
        FreeDirective Exit();
    }
}