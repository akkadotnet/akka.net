//-----------------------------------------------------------------------
// <copyright file="KillSwitch.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Stage;

namespace Akka.Streams
{
    /// <summary>
    /// Creates shared or single kill switches which can be used to control completion of graphs from the outside.
    ///  - The factory <see cref="Shared{T}"/> returns a <see cref="SharedKillSwitch"/> which provides a 
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
        public static SharedKillSwitch Shared(string name) => new SharedKillSwitch(name);

        /// <summary>
        /// Creates a new <see cref="IGraph{TShape}"/> of <see cref="FlowShape{TIn,TOut}"/> that materializes to an external switch that allows external completion
        /// of that unique materialization. Different materializations result in different, independent switches.
        /// 
        /// For a Bidi version see <see cref="SingleBidi{TIn1,TOut1}"/>
        /// </summary>
        public static IGraph<FlowShape<T, T>, UniqueKillSwitch> Single<T>() => UniqueKillSwitchStage<T>.Instance;

        /// <summary>
        /// Creates a new <see cref="IGraph{TShape}"/> of <see cref="BidiShape{TIn1,TIn1,TOut1,TOut1}"/> that materializes to an external switch that allows external completion
        /// of that unique materialization. Different materializations result in different, independent switches.
        /// 
        /// For a Flow version see <see cref="Single{T}"/>
        /// </summary>
        public static IGraph<BidiShape<TIn1, TIn1, TOut1, TOut1>, UniqueKillSwitch> SingleBidi<TIn1, TOut1>
            () => UniqueBidiKillSwitchStage<TIn1, TIn1, TOut1, TOut1>.Instance;


        internal abstract class KillableGraphStageLogic : GraphStageLogic
        {
            private readonly Task _terminationSignal;

            protected KillableGraphStageLogic(Task terminationSignal, Shape shape) : base(shape)
            {
                _terminationSignal = terminationSignal;
            }

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
                public Logic(Task terminationSignal, UniqueKillSwitchStage<T> killSwitch) : base(terminationSignal, killSwitch.Shape)
                {
                    SetHandler(killSwitch.In, onPush: () => Push(killSwitch.Out, Grab(killSwitch.In)));
                    SetHandler(killSwitch.Out, onPull: () => Pull(killSwitch.In));
                }
            }

            #endregion

            public static UniqueKillSwitchStage<T> Instance { get; } = new UniqueKillSwitchStage<T>();

            private UniqueKillSwitchStage()
            {
                Shape = new FlowShape<T, T>(In, Out);
            }

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

        private sealed class UniqueBidiKillSwitchStage<TIn1, TOut1, TIn2, TOut2> :
            GraphStageWithMaterializedValue<BidiShape<TIn1, TOut1, TIn2, TOut2>, UniqueKillSwitch>
        {
            #region Logic

            private sealed class Logic : KillableGraphStageLogic
            {
                public Logic(Task terminationSignal, UniqueBidiKillSwitchStage<TIn1, TOut1, TIn2, TOut2> killSwitch)
                    : base(terminationSignal, killSwitch.Shape)
                {
                    SetHandler(killSwitch.In1,
                        onPush: () => Push(killSwitch.Out1, Grab(killSwitch.In1)),
                        onUpstreamFinish: () => Complete(killSwitch.Out1),
                        onUpstreamFailure: cause => Fail(killSwitch.Out1, cause));

                    SetHandler(killSwitch.In2,
                        onPush: () => Push(killSwitch.Out2, Grab(killSwitch.In2)),
                        onUpstreamFinish: () => Complete(killSwitch.Out2),
                        onUpstreamFailure: cause => Fail(killSwitch.Out2, cause));

                    SetHandler(killSwitch.Out1, 
                        onPull: () => Pull(killSwitch.In1),
                        onDownstreamFinish: () => Cancel(killSwitch.In1));

                    SetHandler(killSwitch.Out2,
                        onPull: () => Pull(killSwitch.In2),
                        onDownstreamFinish: () => Cancel(killSwitch.In2));
                }
            }

            #endregion

            public static UniqueBidiKillSwitchStage<TIn1, TOut1, TIn2, TOut2> Instance { get; } =
                new UniqueBidiKillSwitchStage<TIn1, TOut1, TIn2, TOut2>();

            private UniqueBidiKillSwitchStage()
            {
                Shape = new BidiShape<TIn1, TOut1, TIn2, TOut2>(In1, Out1, In2, Out2);
            }

            protected override Attributes InitialAttributes { get; } = Attributes.CreateName("breaker");

            private Inlet<TIn1> In1 { get; } = new Inlet<TIn1>("KillSwitchBidi.in1");

            private Outlet<TOut1> Out1 { get; } = new Outlet<TOut1>("KillSwitchBidi.out1");

            private Inlet<TIn2> In2 { get; } = new Inlet<TIn2>("KillSwitchBidi.in2");

            private Outlet<TOut2> Out2 { get; } = new Outlet<TOut2>("KillSwitchBidi.out2");

            public override BidiShape<TIn1, TOut1, TIn2, TOut2> Shape { get; }
                
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
    public sealed class UniqueKillSwitch
    {
        private readonly TaskCompletionSource<NotUsed> _promise;

        internal UniqueKillSwitch(TaskCompletionSource<NotUsed> promise)
        {
            _promise = promise;
        }

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
        public void Abort(Exception cause) => _promise.TrySetException(cause);

        public override string ToString() => $"SingleKillSwitch({GetHashCode()})";
    }

    /// <summary>
    /// A <see cref="SharedKillSwitch"/> is a provider for <see cref="IGraph{TShape}"/>s of <see cref="FlowShape{TIn,TOut}"/> that can be completed or failed from the outside.
    ///
    /// A <see cref="IGraph{TShape}"/> returned by the switch can be materialized arbitrary amount of times: every newly materialized<see cref="IGraph{TShape}"/>
    /// belongs to the switch from which it was aquired.Multiple <see cref="SharedKillSwitch"/> instances are isolated from each other,
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
    public sealed class SharedKillSwitch
    {
        #region Flow

        private sealed class SharedKillSwitchFlow<T> :
            GraphStageWithMaterializedValue<FlowShape<T, T>, SharedKillSwitch>
        {
            #region Logic 

            private sealed class Logic : KillSwitches.KillableGraphStageLogic
            {
                public Logic(SharedKillSwitchFlow<T> killSwitchFlow)
                    : base(killSwitchFlow._killSwitch._shutdownPromise.Task, killSwitchFlow.Shape)
                {
                    SetHandler(killSwitchFlow.In, onPush: () => Push(killSwitchFlow.Out, Grab(killSwitchFlow.In)));
                    SetHandler(killSwitchFlow.Out, onPull: () => Pull(killSwitchFlow.In));
                }
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

        internal SharedKillSwitch(string name)
        {
            _name = name;
        }

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
        /// Retrurns a typed Flow of a requested type that will be linked to this <see cref="SharedKillSwitch"/> instance. By invoking
        /// <see cref="Shutdown"/> or <see cref="Abort"/> all running instances of all provided <see cref="IGraph{TShape}"/>s by this
        /// switch will be stopped normally or failed.
        /// </summary>
        /// <returns>A reusable <see cref="IGraph{TShape}"/> that is linked with the switch. The materialized value provided is this switch itself.</returns> 
        /// <typeparam name="T">Type of the elements the Flow will forward</typeparam>
        public IGraph<FlowShape<T, T>, SharedKillSwitch> Flow<T>() => new SharedKillSwitchFlow<T>(this);

        public override string ToString() => $"KillSwitch({_name})";
    }
}
