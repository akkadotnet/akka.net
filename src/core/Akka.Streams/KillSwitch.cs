//-----------------------------------------------------------------------
// <copyright file="KillSwitch.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Stage;

namespace Akka.Streams
{
    /// <summary>
    /// Creates shared or single kill switches which can be used to control completion of graphs from the outside.
    ///  - The factory <see cref="Shared"/> returns a <see cref="SharedKillSwitch"/> which provides a 
    ///    <see cref="IGraph{TShape}"/> of <see cref="FlowShape{TIn,TOut}"/> that can be
    ///    used in arbitrary number of graphs and materializations. The switch simultaneously
    ///    controls completion in all of those graphs.
    ///  - The factory <see cref="Single{T}"/> returns a <see cref="IGraph{TShape}"/> of <see cref="FlowShape{TIn,TOut}"/> 
    ///    that materializes to a <see cref="UniqueKillSwitch"/> which is always unique to that materialized Flow itself.
    /// </summary>
    public static class KillSwitches
    {
        /// <summary>
        /// Creates a new <see cref="SharedKillSwitch"/> with the given name that can be used to control the completion of multiple
        /// streams from the outside simultaneously.
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public static SharedKillSwitch Shared(string name) => new SharedKillSwitch(name);

        /// <summary>
        /// Creates a new <see cref="IGraph{TShape}"/> of <see cref="FlowShape{TIn,TOut}"/> that materializes to an external switch that allows external completion
        /// of that unique materialization. Different materializations result in different, independent switches.
        /// 
        /// For a Bidi version see <see cref="SingleBidi{TIn1,TOut1}"/>
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
        public static IGraph<FlowShape<T, T>, UniqueKillSwitch> Single<T>() => UniqueKillSwitchStage<T>.Instance;

        /// <summary>
        /// Creates a new <see cref="IGraph{TShape}"/> of <see cref="BidiShape{TIn1,TIn1,TOut1,TOut1}"/> that materializes to an external switch that allows external completion
        /// of that unique materialization. Different materializations result in different, independent switches.
        /// 
        /// For a Flow version see <see cref="Single{T}"/>
        /// </summary>
        /// <typeparam name="T1">TBD</typeparam>
        /// <typeparam name="T2">TBD</typeparam>
        /// <returns>TBD</returns>
        public static IGraph<BidiShape<T1, T1, T2, T2>, UniqueKillSwitch> SingleBidi<T1, T2>
            () => UniqueBidiKillSwitchStage<T1, T2>.Instance;

        /// <summary>
        /// Returns a flow, which works like a kill switch stage based on a provided <paramref name="cancellationToken"/>.
        /// Since unlike cancellation tokens, kill switches expose ability to finish a stream either gracefully via
        /// <see cref="IKillSwitch.Shutdown"/> or abruptly via <see cref="IKillSwitch.Abort"/>, this distinction is
        /// handled by specifying <paramref name="cancelGracefully"/> parameter.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cancellationToken">Cancellation token used to create a cancellation flow.</param>
        /// <param name="cancelGracefully">
        /// When set to true, will close stream gracefully via completting the stage.
        /// When set to false, will close stream by failing the stage with <see cref="OperationCanceledException"/>.
        /// </param>
        /// <returns></returns>
        public static IGraph<FlowShape<T, T>, NotUsed> AsFlow<T>(this CancellationToken cancellationToken, bool cancelGracefully = false)
        {
            return new CancellableKillSwitchStage<T>(cancellationToken, cancelGracefully);
        }

        internal sealed class CancellableKillSwitchStage<T> : GraphStage<FlowShape<T, T>>
        {
            #region logic

            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly CancellableKillSwitchStage<T> _stage;
                private CancellationTokenRegistration? _registration = null;

                public Logic(CancellableKillSwitchStage<T> stage)
                    : base(stage.Shape)
                {
                    _stage = stage;
                    SetHandler(stage.Inlet, this);
                    SetHandler(stage.Outlet, this);
                }

                public override void PreStart()
                {
                    if (_stage._cancellationToken.IsCancellationRequested)
                    {
                        if (_stage._cancelGracefully)
                            OnCancelComplete();
                        else 
                            OnCancelFail();
                    }
                    else
                    {
                        var onCancel = _stage._cancelGracefully
                            ? GetAsyncCallback(OnCancelComplete)
                            : GetAsyncCallback(OnCancelFail);

                        _registration = _stage._cancellationToken.Register(onCancel);
                    }
                }

                public override void PostStop()
                {
                    _registration?.Dispose();
                    base.PostStop();
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public override void OnPush() => Push(_stage.Outlet, Grab(_stage.Inlet));

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public override void OnPull() => Pull(_stage.Inlet);

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private void OnCancelComplete() => CompleteStage();

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private void OnCancelFail() => FailStage(new OperationCanceledException($"Stage cancelled due to cancellation token request.", _stage._cancellationToken));
            }

            #endregion

            private readonly CancellationToken _cancellationToken;
            private readonly bool _cancelGracefully;

            public CancellableKillSwitchStage(CancellationToken cancellationToken, bool cancelGracefully)
            {
                _cancellationToken = cancellationToken;
                _cancelGracefully = cancelGracefully;
                Shape = new FlowShape<T, T>(Inlet, Outlet);
            }

            public Inlet<T> Inlet { get; } = new Inlet<T>("cancel.in");
            public Outlet<T> Outlet { get; } = new Outlet<T>("cancel.out");

            public override FlowShape<T, T> Shape { get; }
            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal abstract class KillableGraphStageLogic : InAndOutGraphStageLogic
        {
            private readonly Task _terminationSignal;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="terminationSignal">TBD</param>
            /// <param name="shape">TBD</param>
            protected KillableGraphStageLogic(Task terminationSignal, Shape shape) : base(shape)
            {
                _terminationSignal = terminationSignal;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override void PreStart()
            {
                if (_terminationSignal.IsCompleted)
                    OnSwitch(_terminationSignal);
                else
                    // callback.invoke is a simple actor send, so it is fine to run on the invoking thread
                    _terminationSignal.ContinueWith(t => GetAsyncCallback<Task>(OnSwitch)(t));
            }

            private void OnSwitch(Task t)
            {
                if (_terminationSignal.IsFaulted)
                    FailStage(t.Exception);
                else
                    CompleteStage();
            }
        }

        private sealed class UniqueKillSwitchStage<T> : GraphStageWithMaterializedValue<FlowShape<T, T>, UniqueKillSwitch>
        {

            #region Logic

            private sealed class Logic : KillableGraphStageLogic
            {
                private readonly UniqueKillSwitchStage<T> _stage;

                public Logic(Task terminationSignal, UniqueKillSwitchStage<T> stage) : base(terminationSignal, stage.Shape)
                {
                    _stage = stage;
                    SetHandler(stage.In, this);
                    SetHandler(stage.Out, this);
                }

                public override void OnPush() => Push(_stage.Out, Grab(_stage.In));

                public override void OnPull() => Pull(_stage.In);
            }

            #endregion

            public static UniqueKillSwitchStage<T> Instance { get; } = new UniqueKillSwitchStage<T>();

            private UniqueKillSwitchStage() => Shape = new FlowShape<T, T>(In, Out);

            protected override Attributes InitialAttributes { get; } = Attributes.CreateName("breaker");

            private Inlet<T> In { get; } = new Inlet<T>("KillSwitch.in");

            private Outlet<T> Out { get; } = new Outlet<T>("KillSwitch.out");

            public override FlowShape<T, T> Shape { get; }

            public override ILogicAndMaterializedValue<UniqueKillSwitch> CreateLogicAndMaterializedValue(
                Attributes inheritedAttributes)
            {
                var promise = new TaskCompletionSource<NotUsed>();
                var killSwitch = new UniqueKillSwitch(promise);
                return new LogicAndMaterializedValue<UniqueKillSwitch>(new Logic(promise.Task, this), killSwitch);
            }

            public override string ToString() => "UniqueKillSwitchFlow";
        }

        private sealed class UniqueBidiKillSwitchStage<T1, T2> :
            GraphStageWithMaterializedValue<BidiShape<T1, T1, T2, T2>, UniqueKillSwitch>
        {
            #region Logic

            private sealed class Logic : KillableGraphStageLogic
            {
                private readonly UniqueBidiKillSwitchStage<T1, T2> _killSwitch;

                public Logic(Task terminationSignal, UniqueBidiKillSwitchStage<T1, T2> killSwitch)
                    : base(terminationSignal, killSwitch.Shape)
                {
                    _killSwitch = killSwitch;

                    SetHandler(killSwitch.In1, this);
                    SetHandler(killSwitch.Out1, this);

                    SetHandler(killSwitch.In2,
                        onPush: () => Push(killSwitch.Out2, Grab(killSwitch.In2)),
                        onUpstreamFinish: () => Complete(killSwitch.Out2),
                        onUpstreamFailure: cause => Fail(killSwitch.Out2, cause));

                    SetHandler(killSwitch.Out2,
                        onPull: () => Pull(killSwitch.In2),
                        onDownstreamFinish: () => Cancel(killSwitch.In2));
                }

                public override void OnPush() => Push(_killSwitch.Out1, Grab(_killSwitch.In1));

                public override void OnUpstreamFinish() => Complete(_killSwitch.Out1);

                public override void OnUpstreamFailure(Exception e) => Fail(_killSwitch.Out1, e);

                public override void OnPull() => Pull(_killSwitch.In1);

                public override void OnDownstreamFinish() => Cancel(_killSwitch.In1);
            }

            #endregion

            public static UniqueBidiKillSwitchStage<T1, T2> Instance { get; } = new UniqueBidiKillSwitchStage<T1, T2>();

            private UniqueBidiKillSwitchStage() => Shape = new BidiShape<T1, T1, T2, T2>(In1, Out1, In2, Out2);

            protected override Attributes InitialAttributes { get; } = Attributes.CreateName("breaker");

            private Inlet<T1> In1 { get; } = new Inlet<T1>("KillSwitchBidi.in1");

            private Outlet<T1> Out1 { get; } = new Outlet<T1>("KillSwitchBidi.out1");

            private Inlet<T2> In2 { get; } = new Inlet<T2>("KillSwitchBidi.in2");

            private Outlet<T2> Out2 { get; } = new Outlet<T2>("KillSwitchBidi.out2");

            public override BidiShape<T1, T1, T2, T2> Shape { get; }
                
            public override ILogicAndMaterializedValue<UniqueKillSwitch> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
            {
                var promise = new TaskCompletionSource<NotUsed>();
                var killSwitch = new UniqueKillSwitch(promise);
                var logic = new Logic(promise.Task, this);

                return new LogicAndMaterializedValue<UniqueKillSwitch>(logic, killSwitch);
            }

            public override string ToString() => "UniqueKillSwitchBidi";
        }
    }

    /// <summary>
    /// A <see cref="IKillSwitch"/> allows completion of <see cref="IGraph{TShape}"/>s from the outside by completing 
    /// <see cref="IGraph{TShape}"/>s of <see cref="FlowShape{TIn,TOut}"/> linked to the switch. 
    /// Depending on whether the <see cref="IKillSwitch"/> is a <see cref="UniqueKillSwitch"/> or a <see cref="SharedKillSwitch"/> one or
    /// multiple streams might be linked with the switch. For details see the documentation of the concrete subclasses of
    /// this interface.
    /// </summary>
    public interface IKillSwitch
    {
        /// <summary>
        /// After calling <see cref="Shutdown"/> the linked <see cref="IGraph{TShape}"/>s of <see cref="FlowShape{TIn,TOut}"/> are completed normally.
        /// </summary>
        void Shutdown();

        /// <summary>
        /// After calling <see cref="Abort"/> the linked <see cref="IGraph{TShape}"/>s of <see cref="FlowShape{TIn,TOut}"/> are failed.
        /// </summary>
        /// <param name="cause">TBD</param>
        void Abort(Exception cause);
    }

    /// <summary>
    /// A <see cref="UniqueKillSwitch"/> is always a result of a materialization (unlike <see cref="SharedKillSwitch"/> which is constructed
    /// before any materialization) and it always controls that graph and stage which yielded the materialized value.
    /// 
    /// After calling <see cref="Shutdown"/> the running instance of the <see cref="IGraph{TShape}"/> of <see cref="FlowShape{TIn,TOut}"/> that materialized to the
    /// <see cref="UniqueKillSwitch"/> will complete its downstream and cancel its upstream (unless if finished or failed already in which
    /// case the command is ignored). Subsequent invocations of completion commands will be ignored.
    /// 
    /// After calling <see cref="Abort"/> the running instance of the <see cref="IGraph{TShape}"/> of <see cref="FlowShape{TIn,TOut}"/> that materialized to the
    /// <see cref="UniqueKillSwitch"/> will fail its downstream with the provided exception and cancel its upstream
    /// (unless if finished or failed already in which case the command is ignored). Subsequent invocations of completion commands will be ignored.
    /// 
    /// It is also possible to individually cancel, complete or fail upstream and downstream parts by calling the corresponding methods.
    /// </summary>
    public sealed class UniqueKillSwitch : IKillSwitch
    {
        private readonly TaskCompletionSource<NotUsed> _promise;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="promise">TBD</param>
        internal UniqueKillSwitch(TaskCompletionSource<NotUsed> promise) => _promise = promise;

        /// <summary>
        /// After calling <see cref="Shutdown"/> the running instance of the <see cref="IGraph{TShape}"/> of <see cref="FlowShape{TIn,TOut}"/> that materialized to the
        /// <see cref="UniqueKillSwitch"/> will complete its downstream and cancel its upstream (unless if finished or failed already in which
        /// case the command is ignored). Subsequent invocations of completion commands will be ignored.
        /// </summary>
        public void Shutdown() => _promise.TrySetResult(NotUsed.Instance);


        /// <summary>
        /// After calling <see cref="Abort"/> the running instance of the <see cref="IGraph{TShape}"/> of <see cref="FlowShape{TIn,TOut}"/> that materialized to the
        /// <see cref="UniqueKillSwitch"/> will fail its downstream with the provided exception and cancel its upstream
        /// (unless if finished or failed already in which case the command is ignored). Subsequent invocations of
        /// completion commands will be ignored.
        /// </summary>
        /// <param name="cause">TBD</param>
        public void Abort(Exception cause) => _promise.TrySetException(cause);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"SingleKillSwitch({GetHashCode()})";
    }

    /// <summary>
    /// A <see cref="SharedKillSwitch"/> is a provider for <see cref="IGraph{TShape}"/>s of <see cref="FlowShape{TIn,TOut}"/> that can be completed or failed from the outside.
    ///
    /// A <see cref="IGraph{TShape}"/> returned by the switch can be materialized arbitrary amount of times: every newly materialized<see cref="IGraph{TShape}"/>
    /// belongs to the switch from which it was acquired. Multiple <see cref="SharedKillSwitch"/> instances are isolated from each other,
    /// shutting down or aborting on instance does not affect the <see cref="IGraph{TShape}"/>s provided by another instance.
    ///
    ///
    /// After calling <see cref="Shutdown"/> all materialized, running instances of all <see cref="IGraph{TShape}"/>s provided by the
    /// <see cref="SharedKillSwitch"/> will complete their downstreams and cancel their upstreams(unless if finished or failed already in which
    /// case the command is ignored). Subsequent invocations of <see cref="Shutdown"/> and <see cref="Abort"/> will be
    /// ignored.
    ///
    /// After calling <see cref="Abort"/> all materialized, running instances of all <see cref="IGraph{TShape}"/>s provided by the
    /// <see cref="SharedKillSwitch"/> will fail their downstreams with the provided exception and cancel their upstreams
    /// (unless it finished or failed already in which case the command is ignored). Subsequent invocations of
    /// <see cref="Shutdown"/> and <see cref="Abort"/> will be ignored.
    ///
    /// The <see cref="IGraph{TShape}"/>s provided by the <see cref="SharedKillSwitch"/> do not modify the passed through elements in any way or affect
    /// backpressure in the stream. All provided <see cref="IGraph{TShape}"/>s provide the parent <see cref="SharedKillSwitch"/> as materialized value.
    ///
    /// This class is thread-safe, the instance can be passed safely among threads and its methods may be invoked concurrently.
    /// </summary>
    public sealed class SharedKillSwitch : IKillSwitch
    {
        #region Flow

        private sealed class SharedKillSwitchFlow<T> :
            GraphStageWithMaterializedValue<FlowShape<T, T>, SharedKillSwitch>
        {
            #region Logic 

            private sealed class Logic : KillSwitches.KillableGraphStageLogic
            {
                private readonly SharedKillSwitchFlow<T> _killSwitchFlow;

                public Logic(SharedKillSwitchFlow<T> killSwitchFlow)
                    : base(killSwitchFlow._killSwitch._shutdownPromise.Task, killSwitchFlow.Shape)
                {
                    _killSwitchFlow = killSwitchFlow;
                    SetHandler(killSwitchFlow.In, this);
                    SetHandler(killSwitchFlow.Out, this);
                }

                public override void OnPush() => Push(_killSwitchFlow.Out, Grab(_killSwitchFlow.In));

                public override void OnPull() => Pull(_killSwitchFlow.In);
            }

            #endregion

            private readonly SharedKillSwitch _killSwitch;

            public SharedKillSwitchFlow(SharedKillSwitch killSwitch)
            {
                _killSwitch = killSwitch;
                Shape = new FlowShape<T, T>(In, Out);
            }

            private Inlet<T> In { get; } = new Inlet<T>("KillSwitch.in");

            private Outlet<T> Out { get; } = new Outlet<T>("KillSwitch.out");

            public override FlowShape<T, T> Shape { get; }

            public override ILogicAndMaterializedValue<SharedKillSwitch> CreateLogicAndMaterializedValue(
                    Attributes inheritedAttributes)
                => new LogicAndMaterializedValue<SharedKillSwitch>(new Logic(this), _killSwitch);

            public override string ToString() => $"Flow({_killSwitch})";
        }

        #endregion

        private readonly TaskCompletionSource<NotUsed> _shutdownPromise = new TaskCompletionSource<NotUsed>();
        private readonly string _name;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        internal SharedKillSwitch(string name) => _name = name;

        /// <summary>
        /// After calling <see cref="Shutdown"/> all materialized, running instances of all <see cref="IGraph{TShape}"/>s provided by the
        /// <see cref="SharedKillSwitch"/> will complete their downstreams and cancel their upstreams (unless if finished or failed already in which
        /// case the command is ignored). Subsequent invocations of <see cref="Shutdown"/> and <see cref="Abort"/> will be
        /// ignored.
        /// </summary>
        public void Shutdown() => _shutdownPromise.TrySetResult(NotUsed.Instance);

        /// <summary>
        /// After calling <see cref="Abort"/> all materialized, running instances of all <see cref="IGraph{TShape}"/>s provided by the
        /// <see cref="SharedKillSwitch"/> will fail their downstreams with the provided exception and cancel their upstreams
        /// (unless it finished or failed already in which case the command is ignored). Subsequent invocations of
        /// <see cref="Shutdown"/> and <see cref="Abort"/> will be ignored.
        ///
        /// These provided <see cref="IGraph{TShape}"/>s materialize to their owning switch. This might make certain integrations simpler than
        /// passing around the switch instance itself.
        /// </summary>
        /// <param name="cause">The exception to be used for failing the linked <see cref="IGraph{TShape}"/>s</param>
        public void Abort(Exception cause) => _shutdownPromise.TrySetException(cause);

        /// <summary>
        /// Returns a typed Flow of a requested type that will be linked to this <see cref="SharedKillSwitch"/> instance. By invoking
        /// <see cref="Shutdown"/> or <see cref="Abort"/> all running instances of all provided <see cref="IGraph{TShape}"/>s by this
        /// switch will be stopped normally or failed.
        /// </summary>
        /// <returns>A reusable <see cref="IGraph{TShape}"/> that is linked with the switch. The materialized value provided is this switch itself.</returns> 
        /// <typeparam name="T">Type of the elements the Flow will forward</typeparam>
        /// <returns>TBD</returns>
        public IGraph<FlowShape<T, T>, SharedKillSwitch> Flow<T>() => new SharedKillSwitchFlow<T>(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"KillSwitch({_name})";
    }
}
