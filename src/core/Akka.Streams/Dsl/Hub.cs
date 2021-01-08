//-----------------------------------------------------------------------
// <copyright file="Hub.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Annotations;
using Akka.Streams.Stage;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// A MergeHub is a special streaming hub that is able to collect streamed elements from a dynamic set of
    /// producers. It consists of two parts, a <see cref="Source{TOut,TMat}"/> and a <see cref="Sink{TIn,TMat}"/>. The <see cref="Source{TOut,TMat}"/> streams the element to a consumer from
    /// its merged inputs. Once the consumer has been materialized, the <see cref="Source{TOut,TMat}"/> returns a materialized value which is
    /// the corresponding <see cref="Sink{TIn,TMat}"/>. This <see cref="Sink{TIn,TMat}"/> can then be materialized arbitrary many times, where each of the new
    /// materializations will feed its consumed elements to the original <see cref="Source{TOut,TMat}"/>.
    /// </summary>
    public static class MergeHub
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal const int Cancel = -1;

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> that emits elements merged from a dynamic set of producers. After the <see cref="Source{TOut,TMat}"/> returned
        /// by this method is materialized, it returns a <see cref="Sink{TIn,TMat}"/> as a materialized value. This <see cref="Sink{TIn,TMat}"/> can be materialized
        /// arbitrary many times and each of the materializations will feed the elements into the original <see cref="Source{TOut,TMat}"/>.
        ///
        /// Every new materialization of the <see cref="Source{TOut,TMat}"/> results in a new, independent hub, which materializes to its own
        /// <see cref="Sink{TIn,TMat}"/> for feeding that materialization.
        ///
        /// If one of the inputs fails the <see cref="Sink{TIn,TMat}"/>, the <see cref="Source{TOut,TMat}"/> is failed in turn (possibly jumping over already buffered
        /// elements). Completed <see cref="Sink{TIn,TMat}"/>s are simply removed. Once the <see cref="Source{TOut,TMat}"/> is cancelled, the Hub is considered closed
        /// and any new producers using the <see cref="Sink{TIn,TMat}"/> will be cancelled.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="perProducerBufferSize">Buffer space used per producer. Default value is 16.</param>
        /// <returns>TBD</returns>
        public static Source<T, Sink<T, NotUsed>> Source<T>(int perProducerBufferSize)
            => Dsl.Source.FromGraph(new MergeHub<T>(perProducerBufferSize));

        ///<summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> that emits elements merged from a dynamic set of producers. After the <see cref="Source{TOut,TMat}"/> returned
        /// by this method is materialized, it returns a <see cref="Sink{TIn,TMat}"/> as a materialized value. This <see cref="Sink{TIn,TMat}"/> can be materialized
        /// arbitrary many times and each of the materializations will feed the elements into the original <see cref="Source{TOut,TMat}"/>.
        ///
        /// Every new materialization of the <see cref="Source{TOut,TMat}"/> results in a new, independent hub, which materializes to its own
        /// <see cref="Sink{TIn,TMat}"/> for feeding that materialization.
        ///
        /// If one of the inputs fails the <see cref="Sink{TIn,TMat}"/>, the <see cref="Source{TOut,TMat}"/> is failed in turn (possibly jumping over already buffered
        /// elements). Completed <see cref="Sink{TIn,TMat}"/>s are simply removed. Once the <see cref="Source{TOut,TMat}"/> is cancelled, the Hub is considered closed
        /// and any new producers using the <see cref="Sink{TIn,TMat}"/> will be cancelled.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Source<T, Sink<T, NotUsed>> Source<T>() => Source<T>(16);

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class ProducerFailed : Exception
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ProducerFailed"/> class.
            /// </summary>
            /// <param name="message">The error message that explains the reason for the exception.</param>
            /// <param name="cause">The exception that is the cause of the current exception.</param>
            public ProducerFailed(string message, Exception cause) : base(message, cause)
            {

            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal class MergeHub<T> : GraphStageWithMaterializedValue<SourceShape<T>, Sink<T, NotUsed>>
    {
        #region Internal classes

        private interface IEvent
        {
            long Id { get; }
        }

        private sealed class Element : IEvent
        {
            public Element(long id, T value)
            {
                Id = id;
                Value = value;
            }

            public long Id { get; }

            public T Value { get; }
        }

        private sealed class Register : IEvent
        {
            public Register(long id, Action<long> demandCallback)
            {
                Id = id;
                DemandCallback = demandCallback;
            }

            public long Id { get; }

            public Action<long> DemandCallback { get; }
        }

        private sealed class Deregister : IEvent
        {
            public Deregister(long id)
            {
                Id = id;
            }

            public long Id { get; }
        }

        private sealed class InputState
        {
            private readonly long _demandThreshold;
            private readonly Action<long> _signalDemand;
            private long _untilNextDemandSignal;

            public InputState(Action<long> signalDemand, long demandThreshold)
            {
                _demandThreshold = demandThreshold;
                _signalDemand = signalDemand;
                _untilNextDemandSignal = demandThreshold;
            }

            public void OnElement()
            {
                _untilNextDemandSignal--;
                if (_untilNextDemandSignal == 0)
                {
                    _untilNextDemandSignal = _demandThreshold;
                    _signalDemand(_demandThreshold);
                }
            }

            public void Close() => _signalDemand(MergeHub.Cancel);
        }

        #endregion

        #region Logic

        private sealed class HubLogic : OutGraphStageLogic
        {
            /// <summary>
            /// Basically all merged messages are shared in this queue. Individual buffer sizes are enforced by tracking
            /// demand per producer in the 'demands' Map. One twist here is that the same queue contains control messages,
            /// too. Since the queue is read only if the output port has been pulled, downstream backpressure can delay
            /// processing of control messages. This causes no issues though, see the explanation in 'tryProcessNext'.
            /// </summary>
            private readonly ConcurrentQueue<IEvent> _queue = new ConcurrentQueue<IEvent>();

            private readonly MergeHub<T> _stage;
            private readonly AtomicCounterLong _producerCount;
            private readonly Dictionary<long, InputState> _demands = new Dictionary<long, InputState>();
            private Action _wakeupCallback;
            private bool _needWakeup;
            private bool _shuttingDown;

            public HubLogic(MergeHub<T> stage, AtomicCounterLong producerCount) : base(stage.Shape)
            {
                _stage = stage;
                _producerCount = producerCount;
                SetHandler(stage.Out, this);
            }

            public override void PreStart() => _wakeupCallback = GetAsyncCallback(() =>
            {
                // We are only allowed to dequeue if we are not backpressured. See comment in tryProcessNext() for details.
                if (IsAvailable(_stage.Out))
                    TryProcessNext(true);
            });

            public override void PostStop()
            {
                // First announce that we are shutting down. This will notify late-comers to not even put anything in the queue
                _shuttingDown = true;

                // Anybody that missed the announcement needs to be notified.
                while (_queue.TryDequeue(out var e))
                {
                    var register = e as Register;
                    register?.DemandCallback(MergeHub.Cancel);
                }

                // Kill everyone else
                _demands.Values.ForEach(v => v.Close());
            }

            private bool OnEvent(IEvent e)
            {
                if (e is Element element)
                {
                    _demands[element.Id].OnElement();
                    Push(_stage.Out, element.Value);
                    return false;
                }

                if (e is Register register)
                {
                    _demands.Add(register.Id, new InputState(register.DemandCallback, _stage.DemandThreshold));
                    return true;
                }

                // only Deregister left, no need for a cast.
                _demands.Remove(e.Id);
                return true;
            }

            public override void OnPull() => TryProcessNext(true);

            private void TryProcessNext(bool firstAttempt)
            {
                while (true)
                {
                    // That we dequeue elements from the queue when there is demand means that Register and Deregister messages
                    // might be delayed for arbitrary long. This is not a problem as Register is only interesting if it is followed
                    // by actual elements, which would be delayed anyway by the backpressure.
                    // Unregister is only used to keep the map growing too large, but otherwise it is not critical to process it
                    // timely. In fact, the only way the map could keep growing would mean that we dequeue Registers from the
                    // queue, but then we will eventually reach the Deregister message, too.
                    if (_queue.TryDequeue(out var nextElement))
                    {
                        _needWakeup = false;
                        if (OnEvent(nextElement))
                        {
                            firstAttempt = true;
                            continue;
                        }
                    }
                    else
                    {
                        // additional poll to grab any elements that might missed the needWakeup
                        // and have been enqueued just after it
                        _needWakeup = true;
                        if (firstAttempt)
                        {
                            firstAttempt = false;
                            continue;
                        }
                    }
                    break;
                }
            }

            /// <summary>
            /// External API
            /// </summary>
            public void Enqueue(IEvent ev)
            {
                _queue.Enqueue(ev);

                // Simple volatile var is enough, there is no need for a CAS here. The first important thing to note
                // that we don't care about double-wakeups. Since the "wakeup" is actually handled by an actor message
                // (AsyncCallback) we don't need to handle this case, a double-wakeup will be idempotent (only wasting some cycles).
                //
                // The only case that we care about is a missed wakeup. The characteristics of a missed wakeup are the following:
                //  (1) there is at least one message in the queue
                //  (2) the consumer is not running right now
                //  (3) no wakeupCallbacks are pending
                //  (4) all producers exited this method
                //
                // From the above we can deduce that
                //  (5) needWakeup = true at some point in time. This is implied by (1) and (2) and the
                //      'tryProcessNext' method
                //  (6) There must have been one producer that observed needWakeup = false. This follows from (4) and (3)
                //      and the implementation of this method. In addition, this producer arrived after needWakeup = true,
                //      since before that, every queued elements have been consumed.
                //  (7) There have been at least one producer that observed needWakeup = true and enqueued an element and
                //      a wakeup signal. This follows from (5) and (6), and the fact that either this method sets
                //      needWakeup = false, or the 'tryProcessNext' method, i.e. a wakeup must happened since (5)
                //  (8) If there were multiple producers satisfying (6) take the last one. Due to (6), (3) and (4) we know
                //      there cannot be a wakeup pending, and we just enqueued an element, so (1) holds. Since we are the last
                //      one, (2) must be true or there is no lost wakeup. However, due to (7) we know there was at least one
                //      wakeup (otherwise needWakeup = true). Now, if the consumer is still running (2) is violated,
                //      if not running then needWakeup = false is violated (which comes from (6)). No matter what,
                //      contradiction. QED.

                if (_needWakeup)
                {
                    _needWakeup = false;
                    _wakeupCallback();
                }
            }

            public bool IsShuttingDown => _shuttingDown;
        }

        #endregion

        #region Sink

        private sealed class HubSink : GraphStage<SinkShape<T>>
        {
            #region Logic

            private sealed class SinkLogic : InGraphStageLogic
            {
                private readonly HubSink _stage;
                private readonly HubLogic _logic;
                private readonly long _id;
                private long _demand;

                public SinkLogic(HubSink stage) : base(stage.Shape)
                {
                    _stage = stage;
                    _logic = stage._logic;
                    _id = stage._idCounter.GetAndIncrement();

                    // Start from non-zero demand to avoid initial delays.
                    // The HUB will expect this behavior.
                    _demand = stage._hub._perProducerBufferSize;

                    SetHandler(stage.In, this);
                }

                public override void PreStart()
                {
                    if (!_logic.IsShuttingDown)
                    {
                        _logic.Enqueue(new Register(_id, GetAsyncCallback<long>(OnDemand)));

                        // At this point, we could be in the unfortunate situation that:
                        // - we missed the shutdown announcement and entered this arm of the if statement
                        // - *before* we enqueued our Register event, the Hub already finished looking at the queue
                        //   and is now dead, so we are never notified again.
                        // To safeguard against this, we MUST check the announcement again. This is enough:
                        // if the Hub is no longer looking at the queue, then it must be that isShuttingDown must be already true.
                        if (!_logic.IsShuttingDown)
                            PullWithDemand();
                        else
                            CompleteStage();
                    }
                    else
                        CompleteStage();
                }

                public override void PostStop()
                {
                    // Unlike in the case of preStart, we don't care about the Hub no longer looking at the queue.
                    if (!_logic.IsShuttingDown)
                        _logic.Enqueue(new Deregister(_id));
                }

                public override void OnPush()
                {
                    _logic.Enqueue(new Element(_id, Grab(_stage.In)));
                    if (_demand > 0)
                        PullWithDemand();
                }

                private void PullWithDemand()
                {
                    _demand--;
                    Pull(_stage.In);
                }

                // Make some noise
                public override void OnUpstreamFailure(Exception e)
                {
                    throw new MergeHub.ProducerFailed(
                        "Upstream producer failed with exception, removing from MergeHub now", e);
                }

                private void OnDemand(long moreDemand)
                {
                    if (moreDemand == MergeHub.Cancel)
                        CompleteStage();
                    else
                    {
                        _demand += moreDemand;
                        if (!HasBeenPulled(_stage.In))
                            PullWithDemand();
                    }
                }
            }

            #endregion

            private readonly MergeHub<T> _hub;
            private readonly AtomicCounterLong _idCounter;
            private readonly HubLogic _logic;

            public HubSink(MergeHub<T> hub, AtomicCounterLong idCounter, HubLogic logic)
            {
                _hub = hub;
                _idCounter = idCounter;
                _logic = logic;
                Shape = new SinkShape<T>(In);
            }

            private Inlet<T> In { get; } = new Inlet<T>("MergeHub.in");

            public override SinkShape<T> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new SinkLogic(this);
        }

        #endregion

        private readonly int _perProducerBufferSize;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="perProducerBufferSize">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="perProducerBufferSize"/> is less than or equal to zero.
        /// </exception>
        public MergeHub(int perProducerBufferSize)
        {
            if (perProducerBufferSize <= 0)
                throw new ArgumentException("Buffer size must be positive", nameof(perProducerBufferSize));

            _perProducerBufferSize = perProducerBufferSize;
            DemandThreshold = perProducerBufferSize / 2 + perProducerBufferSize % 2;
            Shape = new SourceShape<T>(Out);
        }

        /// <summary>
        /// Half of buffer size, rounded up
        /// </summary>
        private int DemandThreshold { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<T> Out { get; } = new Outlet<T>("MergeHub.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Sink<T, NotUsed>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var idCounter = new AtomicCounterLong();
            var logic = new HubLogic(this, idCounter);
            var sink = new HubSink(this, idCounter, logic);
            return new LogicAndMaterializedValue<Sink<T, NotUsed>>(logic, Sink.FromGraph(sink));
        }
    }

    /// <summary>
    /// A BroadcastHub is a special streaming hub that is able to broadcast streamed elements to a dynamic set of consumers.
    /// It consists of two parts, a <see cref="Sink{TIn,TMat}"/> and a <see cref="Source{TOut,TMat}"/>. The <see cref="Sink{TIn,TMat}"/> broadcasts elements from a producer to the
    /// actually live consumers it has. Once the producer has been materialized, the <see cref="Sink{TIn,TMat}"/> it feeds into returns a
    /// materialized value which is the corresponding <see cref="Source{TOut,TMat}"/>. This <see cref="Source{TOut,TMat}"/> can be materialized arbitrary many times,
    /// where each of the new materializations will receive their elements from the original <see cref="Sink{TIn,TMat}"/>.
    /// </summary>
    public class BroadcastHub
    {
        /// <summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> that receives elements from its upstream producer and broadcasts them to a dynamic set
        /// of consumers. After the <see cref="Sink{TIn,TMat}"/> returned by this method is materialized, it returns a <see cref="Source{TOut,TMat}"/> as materialized
        /// value. This <see cref="Source{TOut,TMat}"/> can be materialized arbitrary many times and each materialization will receive the
        /// broadcast elements from the original <see cref="Sink{TIn,TMat}"/>.
        ///
        /// Every new materialization of the <see cref="Sink{TIn,TMat}"/> results in a new, independent hub, which materializes to its own
        /// <see cref="Source{TOut,TMat}"/> for consuming the <see cref="Sink{TIn,TMat}"/> of that materialization.
        ///
        /// If the original <see cref="Sink{TIn,TMat}"/> is failed, then the failure is immediately propagated to all of its materialized
        /// <see cref="Source{TOut,TMat}"/>s (possibly jumping over already buffered elements). If the original <see cref="Sink{TIn,TMat}"/> is completed, then
        /// all corresponding <see cref="Source{TOut,TMat}"/>s are completed. Both failure and normal completion is "remembered" and later
        /// materializations of the <see cref="Source{TOut,TMat}"/> will see the same (failure or completion) state. <see cref="Source{TOut,TMat}"/>s that are
        /// cancelled are simply removed from the dynamic set of consumers.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="bufferSize">
        /// Buffer size used by the producer. Gives an upper bound on how "far" from each other two
        /// concurrent consumers can be in terms of element. If this buffer is full, the producer
        /// is backpressured. Must be a power of two and less than 4096.
        /// </param>
        /// <returns>TBD</returns>
        public static Sink<T, Source<T, NotUsed>> Sink<T>(int bufferSize)
            => Dsl.Sink.FromGraph(new BroadcastHub<T>(bufferSize));


        /// <summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> that receives elements from its upstream producer and broadcasts them to a dynamic set
        /// of consumers. After the <see cref="Sink{TIn,TMat}"/> returned by this method is materialized, it returns a <see cref="Source{TOut,TMat}"/> as materialized
        /// value. This <see cref="Source{TOut,TMat}"/> can be materialized arbitrary many times and each materialization will receive the
        /// broadcast elements from the original <see cref="Sink{TIn,TMat}"/>.
        ///
        /// Every new materialization of the <see cref="Sink{TIn,TMat}"/> results in a new, independent hub, which materializes to its own
        /// <see cref="Source{TOut,TMat}"/> for consuming the <see cref="Sink{TIn,TMat}"/> of that materialization.
        ///
        /// If the original <see cref="Sink{TIn,TMat}"/> is failed, then the failure is immediately propagated to all of its materialized
        /// <see cref="Source{TOut,TMat}"/>s (possibly jumping over already buffered elements). If the original <see cref="Sink{TIn,TMat}"/> is completed, then
        /// all corresponding <see cref="Source{TOut,TMat}"/>s are completed. Both failure and normal completion is "remembered" and later
        /// materializations of the <see cref="Source{TOut,TMat}"/> will see the same (failure or completion) state. <see cref="Source{TOut,TMat}"/>s that are
        /// cancelled are simply removed from the dynamic set of consumers.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Sink<T, Source<T, NotUsed>> Sink<T>() => Sink<T>(256);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class BroadcastHub<T> : GraphStageWithMaterializedValue<SinkShape<T>, Source<T, NotUsed>>
    {
        #region internal classes

        private interface IHubEvent { }

        private sealed class RegistrationPending : IHubEvent
        {
            public static RegistrationPending Instance { get; } = new RegistrationPending();

            private RegistrationPending()
            {

            }
        }

        private sealed class UnRegister : IHubEvent
        {
            public UnRegister(long id, int previousOffset, int finalOffset)
            {
                Id = id;
                PreviousOffset = previousOffset;
                FinalOffset = finalOffset;
            }

            public long Id { get; }

            public int PreviousOffset { get; }

            public int FinalOffset { get; }
        }

        private sealed class Advanced : IHubEvent
        {
            public Advanced(long id, int previousOffset)
            {
                Id = id;
                PreviousOffset = previousOffset;
            }

            public long Id { get; }

            public int PreviousOffset { get; }
        }

        private sealed class NeedWakeup : IHubEvent
        {
            public NeedWakeup(long id, int previousOffset, int currentOffset)
            {
                Id = id;
                PreviousOffset = previousOffset;
                CurrentOffset = currentOffset;
            }

            public long Id { get; }

            public int PreviousOffset { get; }

            public int CurrentOffset { get; }
        }

        private sealed class Consumer
        {
            public Consumer(long id, Action<IConsumerEvent> callback)
            {
                Id = id;
                Callback = callback;
            }

            public long Id { get; }

            public Action<IConsumerEvent> Callback { get; }
        }


        private sealed class Completed
        {
            public static Completed Instance { get; } = new Completed();

            private Completed()
            {

            }
        }


        private interface IHubState { }

        private sealed class Open : IHubState
        {
            public Open(Task<Action<IHubEvent>> callbackTask, ImmutableList<Consumer> registrations)
            {
                CallbackTask = callbackTask;
                Registrations = registrations;
            }

            public Task<Action<IHubEvent>> CallbackTask { get; }

            public ImmutableList<Consumer> Registrations { get; }
        }

        private sealed class Closed : IHubState
        {
            public Closed(Exception failure = null)
            {
                Failure = failure;
            }

            public Exception Failure { get; }
        }


        private interface IConsumerEvent { }

        private sealed class Wakeup : IConsumerEvent
        {
            public static Wakeup Instance { get; } = new Wakeup();

            private Wakeup()
            {

            }
        }

        private sealed class HubCompleted : IConsumerEvent
        {
            public HubCompleted(Exception failure = null)
            {
                Failure = failure;
            }

            public Exception Failure { get; }
        }

        private sealed class Initialize : IConsumerEvent
        {
            public Initialize(int offset)
            {
                Offset = offset;
            }

            public int Offset { get; }
        }

        #endregion

        #region Logic 

        private sealed class HubLogic : InGraphStageLogic
        {
            private readonly BroadcastHub<T> _stage;

            private readonly TaskCompletionSource<Action<IHubEvent>> _callbackCompletion =
                new TaskCompletionSource<Action<IHubEvent>>();

            private readonly Open _noRegistrationState;
            internal readonly AtomicReference<IHubState> State;

            // Start from values that will almost immediately overflow.This has no effect on performance, any starting
            // number will do, however, this protects from regressions as these values *almost surely* overflow and fail
            // tests if someone makes a mistake.
            private int _tail = int.MaxValue;
            private int _head = int.MaxValue;

            // An Array with a published tail ("latest message") and a privately maintained head ("earliest buffered message").
            // Elements are published by simply putting them into the array and bumping the tail. If necessary, certain
            // consumers are sent a wakeup message through an AsyncCallback.
            private readonly object[] _queue;

            // This is basically a classic Bucket Queue: https://en.wikipedia.org/wiki/Bucket_queue
            // (in fact, this is the variant described in the Optimizations section, where the given set
            // of priorities always fall to a range
            //
            // This wheel tracks the position of Consumers relative to the slowest ones. Every slot
            // contains a list of Consumers being known at that location (this might be out of date!).
            // Consumers from time to time send Advance messages to indicate that they have progressed
            // by reading from the broadcast queue. Consumers that are blocked (due to reaching tail) request
            // a wakeup and update their position at the same time.
            private readonly ImmutableList<Consumer>[] _consumerWheel;

            private int _activeConsumer;

            public HubLogic(BroadcastHub<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _noRegistrationState = new Open(_callbackCompletion.Task, ImmutableList<Consumer>.Empty);
                State = new AtomicReference<IHubState>(_noRegistrationState);
                _queue = new object[stage._bufferSize];
                _consumerWheel = Enumerable.Repeat(0, stage._bufferSize * 2)
                    .Select(_ => ImmutableList<Consumer>.Empty)
                    .ToArray();

                SetHandler(stage.In, this);
            }

            public override void PreStart()
            {
                SetKeepGoing(true);
                _callbackCompletion.SetResult(GetAsyncCallback<IHubEvent>(OnEvent));
                Pull(_stage.In);
            }

            public override void OnUpstreamFinish()
            {
                // Cannot complete immediately if there is no space in the queue to put the completion marker
                if (!IsFull)
                    Complete();
            }

            public override void OnPush()
            {
                Publish(Grab(_stage.In));
                if (!IsFull)
                    Pull(_stage.In);
            }

            private void OnEvent(IHubEvent hubEvent)
            {
                if (hubEvent == RegistrationPending.Instance)
                {
                    var open = (Open)State.GetAndSet(_noRegistrationState);
                    foreach (var c in open.Registrations)
                    {
                        var startFrom = _head;
                        _activeConsumer++;
                        AddConsumer(c, startFrom);
                        c.Callback(new Initialize(startFrom));
                    }

                    return;
                }

                if (hubEvent is UnRegister unregister)
                {
                    _activeConsumer--;
                    FindAndRemoveConsumer(unregister.Id, unregister.PreviousOffset);
                    if (_activeConsumer == 0)
                    {
                        if (IsClosed(_stage.In))
                            CompleteStage();
                        else if (_head != unregister.FinalOffset)
                        {
                            // If our final consumer goes away, we roll forward the buffer so a subsequent consumer does not
                            // see the already consumed elements. This feature is quite handy.

                            while (_head != unregister.FinalOffset)
                            {
                                _queue[_head & _stage._mask] = null;
                                _head++;
                            }

                            _head = unregister.FinalOffset;
                            if (!HasBeenPulled(_stage.In))
                                Pull(_stage.In);
                        }
                    }
                    else
                        CheckUnblock(unregister.PreviousOffset);
                    return;
                }

                if (hubEvent is Advanced advance)
                {
                    var newOffset = advance.PreviousOffset + _stage._demandThreshold;
                    // Move the consumer from its last known offset to its new one. Check if we are unblocked.
                    var c = FindAndRemoveConsumer(advance.Id, advance.PreviousOffset);
                    AddConsumer(c, newOffset);
                    CheckUnblock(advance.PreviousOffset);
                    return;
                }

                // only NeedWakeup left
                var wakeup = (NeedWakeup)hubEvent;
                // Move the consumer from its last known offset to its new one. Check if we are unblocked.
                var consumer = FindAndRemoveConsumer(wakeup.Id, wakeup.PreviousOffset);
                AddConsumer(consumer, wakeup.CurrentOffset);

                // Also check if the consumer is now unblocked since we published an element since it went asleep.
                if (wakeup.CurrentOffset != _tail)
                    consumer.Callback(Wakeup.Instance);
                CheckUnblock(wakeup.PreviousOffset);
            }

            // Producer API
            // We are full if the distance between the slowest (known) consumer and the fastest (known) consumer is
            // the buffer size. We must wait until the slowest either advances, or cancels.
            private bool IsFull => _tail - _head == _stage._bufferSize;

            public override void OnUpstreamFailure(Exception e)
            {
                var failMessage = new HubCompleted(e);

                // Notify pending consumers and set tombstone
                var open = (Open)State.GetAndSet(new Closed(e));
                open.Registrations.ForEach(c => c.Callback(failMessage));

                // Notify registered consumers
                _consumerWheel.SelectMany(x => x).ForEach(c => c.Callback(failMessage));

                FailStage(e);
            }

            /// <summary>
            /// This method removes a consumer with a given ID from the known offset and returns it.
            /// NB: You cannot remove a consumer without knowing its last offset! Consumers on the Source side always must
            /// track this so this can be a fast operation.
            /// </summary>
            private Consumer FindAndRemoveConsumer(long id, int offset)
            {
                // TODO: Try to eliminate modulo division somehow...
                var wheelSlot = offset & _stage._wheelMask;
                var consumerInSlot = _consumerWheel[wheelSlot];
                var remainingConsumersInSlot = new List<Consumer>();
                Consumer removedConsumer = null;

                while (!consumerInSlot.IsEmpty)
                {
                    var consumer = consumerInSlot.First();
                    if (consumer.Id != id)
                        remainingConsumersInSlot.Add(consumer);
                    else
                        removedConsumer = consumer;
                    consumerInSlot = consumerInSlot.Skip(1).ToImmutableList();
                }

                _consumerWheel[wheelSlot] = remainingConsumersInSlot.ToImmutableList();
                return removedConsumer;
            }

            /// <summary>
            /// After removing a Consumer from a wheel slot (because it cancelled, or we moved it because it advanced)
            /// we need to check if it was blocking us from advancing (being the slowest)
            /// </summary>
            private void CheckUnblock(int offsetOfConsumerRemoved)
            {
                if (UnblockIfPossible(offsetOfConsumerRemoved))
                {
                    if (IsClosed(_stage.In))
                        Complete();
                    else if (!HasBeenPulled(_stage.In))
                        Pull(_stage.In);
                }
            }

            private bool UnblockIfPossible(int offsetOfConsumerRemoved)
            {
                var unblocked = false;

                if (offsetOfConsumerRemoved == _head)
                {
                    // Try to advance along the wheel. We can skip any wheel slots which have no waiting Consumers, until
                    // we either find a nonempty one, or we reached the end of the buffer.
                    while (_consumerWheel[_head & _stage._wheelMask].IsEmpty && _head != _tail)
                    {
                        _queue[_head & _stage._mask] = null;
                        _head++;
                        unblocked = true;
                    }
                }

                return unblocked;
            }

            private void AddConsumer(Consumer consumer, int offset)
            {
                var slot = offset & _stage._wheelMask;
                _consumerWheel[slot] = _consumerWheel[slot].Insert(0, consumer);
            }

            /// <summary>
            /// Send a wakeup signal to all the Consumers at a certain wheel index. Note, this needs the actual index,
            /// which is offset modulo (bufferSize + 1).
            /// </summary>
            /// <param name="index">TBD</param>
            private void WakeupIndex(int index)
                => _consumerWheel[index].ForEach(c => c.Callback(Wakeup.Instance));

            private void Complete()
            {
                var index = _tail & _stage._mask;
                var wheelSlot = _tail & _stage._wheelMask;
                _queue[index] = Completed.Instance;
                WakeupIndex(wheelSlot);
                _tail++;
                if (_activeConsumer == 0)
                {
                    // Existing consumers have already consumed all elements and will see completion status in the queue
                    CompleteStage();
                }
            }

            public override void PostStop()
            {
                while (true)
                {
                    // Notify pending consumers and set tombstone
                    if (State.Value is Open open)
                    {
                        if (State.CompareAndSet(open, new Closed()))
                        {
                            var completedMessage = new HubCompleted();
                            foreach (var consumer in open.Registrations)
                                consumer.Callback(completedMessage);
                        }
                        else
                            continue;
                    }
                    // Already closed, ignore
                    break;
                }
            }

            private void Publish(T element)
            {
                var index = _tail & _stage._mask;
                var wheelSlot = _tail & _stage._wheelMask;
                _queue[index] = element;
                // Publish the new tail before calling the wakeup
                _tail++;
                WakeupIndex(wheelSlot);
            }

            /// <summary>
            /// Consumer API
            /// </summary>
            public object Poll(int offset) => offset == _tail ? null : _queue[offset & _stage._mask];
        }

        private sealed class HubSourceLogic : GraphStage<SourceShape<T>>
        {
            private readonly BroadcastHub<T> _hub;
            private readonly HubLogic _hubLogic;
            private readonly AtomicCounterLong _counter;

            private sealed class Logic : OutGraphStageLogic
            {
                private readonly HubSourceLogic _stage;
                private readonly long _id;
                private int _untilNextAdvanceSignal;
                private bool _offsetInitialized;
                private Action<IHubEvent> _hubCallback;

                // We need to track our last offset that we published to the Hub. The reason is, that for efficiency reasons,
                // the Hub can only look up and move/remove Consumers with known wheel slots. This means that no extra hash-map
                // is needed, but it also means that we need to keep track of both our current offset, and the last one that
                // we published.
                private int _previousPublishedOffset;
                private int _offset;

                public Logic(HubSourceLogic stage, long id) : base(stage.Shape)
                {
                    _stage = stage;
                    _id = id;
                    _untilNextAdvanceSignal = stage._hub._demandThreshold;

                    SetHandler(stage.Out, this);
                }

                public override void PreStart()
                {
                    var callback = GetAsyncCallback<IConsumerEvent>(OnCommand);

                    void OnHubReady(Result<Action<IHubEvent>> result)
                    {
                        if (result.IsSuccess)
                        {
                            _hubCallback = result.Value;
                            if (IsAvailable(_stage.Out) && _offsetInitialized)
                                OnPull();
                            _hubCallback(RegistrationPending.Instance);
                        }
                        else
                            FailStage(result.Exception);
                    }

                    /*
                     * Note that there is a potential race here. First we add ourselves to the pending registrations, then
                     * we send RegistrationPending. However, another downstream might have triggered our registration by its
                     * own RegistrationPending message, since we are in the list already.
                     * This means we might receive an onCommand(Initialize(offset)) *before* onHubReady fires so it is important
                     * to only serve elements after both offsetInitialized = true and hubCallback is not null.
                     */
                    while (true)
                    {
                        var state = _stage._hubLogic.State.Value;
                        if (state is Closed closed)
                        {
                            if (closed.Failure != null)
                                FailStage(closed.Failure);
                            else
                                CompleteStage();

                            break;
                        }

                        if (state is Open open)
                        {
                            var newRegistrations = open.Registrations.Insert(0, new Consumer(_id, callback));
                            if (_stage._hubLogic.State.CompareAndSet(state, new Open(open.CallbackTask, newRegistrations)))
                            {
                                var readyCallback = GetAsyncCallback((Action<Result<Action<IHubEvent>>>)OnHubReady);
                                open.CallbackTask.ContinueWith(t => readyCallback(Result.FromTask(t)));
                                break;
                            }

                            continue;
                        }

                        break;
                    }
                }

                public override void OnPull()
                {
                    if (_offsetInitialized && _hubCallback != null)
                    {
                        var element = _stage._hubLogic.Poll(_offset);

                        if (element == null)
                        {
                            _hubCallback(new NeedWakeup(_id, _previousPublishedOffset, _offset));
                            _previousPublishedOffset = _offset;
                            _untilNextAdvanceSignal = _stage._hub._demandThreshold;
                        }
                        else if (element == Completed.Instance)
                            CompleteStage();
                        else
                        {
                            Push(_stage.Out, (T)element);
                            _offset++;
                            _untilNextAdvanceSignal--;
                            if (_untilNextAdvanceSignal == 0)
                            {
                                _untilNextAdvanceSignal = _stage._hub._demandThreshold;
                                var previousOffset = _previousPublishedOffset;
                                _previousPublishedOffset += _stage._hub._demandThreshold;
                                _hubCallback(new Advanced(_id, previousOffset));
                            }
                        }
                    }
                }

                public override void PostStop()
                    => _hubCallback?.Invoke(new UnRegister(_id, _previousPublishedOffset, _offset));

                private void OnCommand(IConsumerEvent e)
                {
                    if (e is HubCompleted completed)
                    {
                        if (completed.Failure != null)
                            FailStage(completed.Failure);
                        else
                            CompleteStage();
                    }
                    else if (e is Wakeup)
                    {
                        if (IsAvailable(_stage.Out))
                            OnPull();
                    }
                    else
                    {
                        var intialize = (Initialize)e;
                        _offsetInitialized = true;
                        _previousPublishedOffset = intialize.Offset;
                        _offset = intialize.Offset;
                        if (IsAvailable(_stage.Out) && _hubCallback != null)
                            OnPull();
                    }
                }
            }

            public HubSourceLogic(BroadcastHub<T> hub, HubLogic hubLogic, AtomicCounterLong counter)
            {
                _hub = hub;
                _hubLogic = hubLogic;
                _counter = counter;
                Shape = new SourceShape<T>(Out);
            }

            private Outlet<T> Out { get; } = new Outlet<T>("HubSourceLogic.out");

            public override SourceShape<T> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
                => new Logic(this, _counter.IncrementAndGet());
        }

        #endregion

        private readonly int _bufferSize;
        private readonly int _mask;
        private readonly int _wheelMask;
        private readonly int _demandThreshold;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="bufferSize">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the specified <paramref name="bufferSize"/>
        /// is less than or equal to zero, is greater than 4095, or is not a power of two.
        /// </exception>
        public BroadcastHub(int bufferSize)
        {
            if (bufferSize <= 0)
                throw new ArgumentException("Buffer must be positive", nameof(bufferSize));
            if (bufferSize > 4095)
                throw new ArgumentException("Buffer size larger then 4095 is not allowed", nameof(bufferSize));
            if ((bufferSize & bufferSize - 1) != 0)
                throw new ArgumentException("Buffer size must be a power of two", nameof(bufferSize));

            _bufferSize = bufferSize;
            _mask = _bufferSize - 1;
            _wheelMask = bufferSize * 2 - 1;

            // Half of buffer size, rounded up
            _demandThreshold = bufferSize / 2 + bufferSize % 2;

            Shape = new SinkShape<T>(In);
        }

        private Inlet<T> In { get; } = new Inlet<T>("BroadcastHub.in");

        /// <summary>
        /// TBD
        /// </summary>
        public override SinkShape<T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Source<T, NotUsed>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var idCounter = new AtomicCounterLong();
            var logic = new HubLogic(this);
            var source = new HubSourceLogic(this, logic, idCounter);

            return new LogicAndMaterializedValue<Source<T, NotUsed>>(logic, Source.FromGraph(source));
        }
    }

    /// <summary>
    /// A <see cref="PartitionHub"/> is a special streaming hub that is able to route streamed elements to a dynamic set of consumers.
    /// It consists of two parts, a <see cref="Sink{TIn,TMat}"/> and a <see cref="Source{TOut,TMat}"/>. The <see cref="Sink{TIn,TMat}"/> e elements from a producer to the
    /// actually live consumers it has.The selection of consumer is done with a function. Each element can be routed to
    /// only one consumer.Once the producer has been materialized, the <see cref="Sink{TIn,TMat}"/> it feeds into returns a
    /// materialized value which is the corresponding <see cref="Source{TOut,TMat}"/>. This <see cref="Source{TOut,TMat}"/> can be materialized an arbitrary number
    /// of times, where each of the new materializations will receive their elements from the original <see cref="Sink{TIn,TMat}"/>.
    /// </summary>
    public static class PartitionHub
    {
        private const int DefaultBufferSize = 256;

        /// <summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> that receives elements from its upstream producer and routes them to a dynamic set
        /// of consumers.After the <see cref="Sink{TIn,TMat}"/> returned by this method is materialized, it returns a 
        /// <see cref="Source{TOut,TMat}"/> as materialized  value.
        /// This <see cref="Source{TOut,TMat}"/> can be materialized an arbitrary number of times and each materialization will receive the
        /// elements from the original <see cref="Sink{TIn,TMat}"/>.
        /// <para/>
        /// Every new materialization of the <see cref="Sink{TIn,TMat}"/> results in a new, independent hub, which materializes to its own
        /// <see cref="Source{TOut,TMat}"/> for consuming the <see cref="Sink{TIn,TMat}"/> of that materialization.
        /// <para/>
        /// If the original <see cref="Sink{TIn,TMat}"/> is failed, then the failure is immediately propagated to all of its materialized
        /// <see cref="Source{TOut,TMat}"/>s (possibly jumping over already buffered elements). If the original <see cref="Sink{TIn,TMat}"/> is completed, then
        /// all corresponding <see cref="Source{TOut,TMat}"/>s are completed.Both failure and normal completion is "remembered" and later
        /// materializations of the <see cref="Source{TOut,TMat}"/> will see the same (failure or completion) state. <see cref="Source{TOut,TMat}"/>s that are
        /// cancelled are simply removed from the dynamic set of consumers.
        /// <para/>
        /// This <see cref="StatefulSink{T}"/> should be used when there is a need to keep mutable state in the partition function,
        /// e.g. for implemening round-robin or sticky session kind of routing. If state is not needed the <see cref="Sink{T}"/> can
        /// be more convenient to use.
        /// </summary>
        /// <param name="partitioner">
        /// Function that decides where to route an element.It is a factory of a function to
        /// to be able to hold stateful variables that are unique for each materialization.The function
        /// takes two parameters; the first is information about active consumers, including an array of consumer
        /// identifiers and the second is the stream element.The function should return the selected consumer
        /// identifier for the given element.The function will never be called when there are no active consumers,
        /// i.e.there is always at least one element in the array of identifiers.
        /// </param>
        /// <param name="startAfterNrOfConsumers">
        /// Elements are buffered until this number of consumers have been connected.
        /// This is only used initially when the stage is starting up, i.e.it is not honored when consumers have been removed (canceled).
        /// </param>
        /// <param name="bufferSize">Total number of elements that can be buffered. If this buffer is full, the producer is backpressured.</param>
        [ApiMayChange]
        public static Sink<T, Source<T, NotUsed>> StatefulSink<T>(Func<Func<IConsumerInfo, T, long>> partitioner,
            int startAfterNrOfConsumers, int bufferSize = DefaultBufferSize)
        {
            return Dsl.Sink.FromGraph(new PartitionHub<T>(partitioner, startAfterNrOfConsumers, bufferSize));
        }

        /// <summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> that receives elements from its upstream producer and routes them to a dynamic set
        /// of consumers.After the <see cref="Sink{TIn,TMat}"/> returned by this method is materialized, it returns a 
        /// <see cref="Source{TOut,TMat}"/> as materialized  value.
        /// This <see cref="Source{TOut,TMat}"/> can be materialized an arbitrary number of times and each materialization will receive the
        /// elements from the original <see cref="Sink{TIn,TMat}"/>.
        /// <para/>
        /// Every new materialization of the <see cref="Sink{TIn,TMat}"/> results in a new, independent hub, which materializes to its own
        /// <see cref="Source{TOut,TMat}"/> for consuming the <see cref="Sink{TIn,TMat}"/> of that materialization.
        /// <para/>
        /// If the original <see cref="Sink{TIn,TMat}"/> is failed, then the failure is immediately propagated to all of its materialized
        /// <see cref="Source{TOut,TMat}"/>s (possibly jumping over already buffered elements). If the original <see cref="Sink{TIn,TMat}"/> is completed, then
        /// all corresponding <see cref="Source{TOut,TMat}"/>s are completed.Both failure and normal completion is "remembered" and later
        /// materializations of the <see cref="Source{TOut,TMat}"/> will see the same (failure or completion) state. <see cref="Source{TOut,TMat}"/>s that are
        /// cancelled are simply removed from the dynamic set of consumers.
        /// <para/>
        /// This <see cref="Sink{T}"/> should be used when the routing function is stateless, e.g. based on a hashed value of the
        /// elements. Otherwise the <see cref="StatefulSink{T}"/> can be used to implement more advanced routing logic.
        /// </summary>
        /// <param name="partitioner">
        /// Function that decides where to route an element. The function takes two parameters;
        /// the first is the number of active consumers and the second is the stream element. The function should
        /// return the index of the selected consumer for the given element, i.e. int greater than or equal to 0
        /// and less than number of consumers. E.g. `(size, elem) => math.abs(elem.hashCode) % size`.
        /// </param>
        /// <param name="startAfterNrOfConsumers">
        /// Elements are buffered until this number of consumers have been connected.
        /// This is only used initially when the stage is starting up, i.e.it is not honored when consumers have been removed (canceled).
        /// </param>
        /// <param name="bufferSize">Total number of elements that can be buffered. If this buffer is full, the producer is backpressured.</param>
        [ApiMayChange]
        public static Sink<T, Source<T, NotUsed>> Sink<T>(Func<int, T, int> partitioner,
            int startAfterNrOfConsumers, int bufferSize = DefaultBufferSize)
        {
            return StatefulSink<T>(() => ((info, element) => info.ConsumerByIndex(partitioner(info.Size, element))),
                startAfterNrOfConsumers, bufferSize);
        }

        /// <summary>
        /// DO NOT INHERIT
        /// </summary>
        [ApiMayChange]
        public interface IConsumerInfo
        {
            /// <summary>
            /// Sequence of all identifiers of current consumers.
            /// 
            /// Use this method only if you need to enumerate consumer existing ids.
            /// When selecting a specific consumerId by its index, prefer using the dedicated <see cref="ConsumerByIndex"/> method instead,
            /// which is optimised for this use case.
            /// </summary>
            ImmutableArray<long> ConsumerIds { get; }

            /// <summary>
            /// Obtain consumer identifier by index 
            /// </summary>
            long ConsumerByIndex(int index);

            /// <summary>
            /// Approximate number of buffered elements for a consumer.
            /// Larger value than other consumers could be an indication of that the consumer is slow.
            /// <para/>
            /// Note that this is a moving target since the elements are consumed concurrently.
            /// </summary>
            int QueueSize(long consumerId);

            /// <summary>
            /// Number of attached consumers.
            /// </summary>
            int Size { get; }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class PartitionHub<T> : GraphStageWithMaterializedValue<SinkShape<T>, Source<T, NotUsed>>
    {
        #region queue implementation

        private interface IPartitionQueue
        {
            void Init(long id);
            int TotalSize { get; }
            int Size(long id);
            bool IsEmpty(long id);
            bool NonEmpty(long id);
            void Offer(long id, object element);
            object Poll(long id);
            void Remove(long id);
        }

        private sealed class ConsumerQueue
        {
            public static ConsumerQueue Empty { get; } = new ConsumerQueue(ImmutableQueue<object>.Empty, 0);

            private readonly ImmutableQueue<object> _queue;

            public ConsumerQueue(ImmutableQueue<object> queue, int size)
            {
                _queue = queue;
                Size = size;
            }

            public ConsumerQueue Enqueue(object element) => new ConsumerQueue(_queue.Enqueue(element), Size + 1);

            public bool IsEmpty => Size == 0;

            public object Head => _queue.First();

            public ConsumerQueue Tail => new ConsumerQueue(_queue.Dequeue(), Size - 1);

            public int Size { get; }
        }

        private sealed class PartitionQueue : IPartitionQueue
        {
            private readonly AtomicCounter _totalSize = new AtomicCounter();
            private readonly ConcurrentDictionary<long, ConsumerQueue> _queues = new ConcurrentDictionary<long, ConsumerQueue>();

            public void Init(long id) => _queues.TryAdd(id, ConsumerQueue.Empty);

            public int TotalSize => _totalSize.Current;

            public int Size(long id)
            {
                if (_queues.TryGetValue(id, out var queue))
                    return queue.Size;

                throw new ArgumentException($"Invalid stream identifier: {id}", nameof(id));
            }

            public bool IsEmpty(long id)
            {
                if (_queues.TryGetValue(id, out var queue))
                    return queue.IsEmpty;

                throw new ArgumentException($"Invalid stream identifier: {id}", nameof(id));
            }

            public bool NonEmpty(long id) => !IsEmpty(id);

            public void Offer(long id, object element)
            {
                if (_queues.TryGetValue(id, out var queue))
                {
                    if (_queues.TryUpdate(id, queue.Enqueue(element), queue))
                        _totalSize.IncrementAndGet();
                    else
                        Offer(id, element);
                }
                else
                    throw new ArgumentException($"Invalid stream identifier: {id}", nameof(id));
            }

            public object Poll(long id)
            {
                var success = _queues.TryGetValue(id, out var queue);
                if (!success || queue.IsEmpty)
                    return null;

                if (_queues.TryUpdate(id, queue.Tail, queue))
                {
                    _totalSize.Decrement();
                    return queue.Head;
                }

                return Poll(id);
            }

            public void Remove(long id)
            {
                if (_queues.TryRemove(id, out var queue))
                    _totalSize.AddAndGet(-queue.Size);
            }
        }

        #endregion

        #region internal classes

        private interface IConsumerEvent { }

        private sealed class Wakeup : IConsumerEvent
        {
            public static Wakeup Instance { get; } = new Wakeup();

            private Wakeup() { }
        }

        private sealed class Initialize : IConsumerEvent
        {
            public static Initialize Instance { get; } = new Initialize();

            private Initialize() { }
        }

        private sealed class HubCompleted : IConsumerEvent
        {
            public Exception Failure { get; }

            public HubCompleted(Exception failure)
            {
                Failure = failure;
            }
        }


        private interface IHubEvent { }

        private sealed class RegistrationPending : IHubEvent
        {
            public static RegistrationPending Instance { get; } = new RegistrationPending();

            private RegistrationPending() { }
        }

        private sealed class UnRegister : IHubEvent
        {
            public long Id { get; }

            public UnRegister(long id)
            {
                Id = id;
            }
        }

        private sealed class NeedWakeup : IHubEvent
        {
            public Consumer Consumer { get; }

            public NeedWakeup(Consumer consumer)
            {
                Consumer = consumer;
            }

        }

        private sealed class Consumer : IHubEvent
        {
            public long Id { get; }
            public Action<IConsumerEvent> Callback { get; }

            public Consumer(long id, Action<IConsumerEvent> callback)
            {
                Id = id;
                Callback = callback;
            }
        }

        private sealed class TryPull : IHubEvent
        {
            public static TryPull Instance { get; } = new TryPull();

            private TryPull() { }
        }

        private sealed class Completed
        {
            public static Completed Instance { get; } = new Completed();

            private Completed() { }
        }


        private interface IHubState { }

        private sealed class Open : IHubState
        {
            public Task<Action<IHubEvent>> CallbackTask { get; }
            public ImmutableList<Consumer> Registrations { get; }

            public Open(Task<Action<IHubEvent>> callbackTask, ImmutableList<Consumer> registrations)
            {
                CallbackTask = callbackTask;
                Registrations = registrations;
            }
        }

        private sealed class Closed : IHubState
        {
            public Exception Failure { get; }

            public Closed(Exception failure)
            {
                Failure = failure;
            }
        }

        #endregion  

        private sealed class PartitionSinkLogic : InGraphStageLogic
        {
            private sealed class ConsumerInfo : PartitionHub.IConsumerInfo
            {
                private readonly PartitionSinkLogic _partitionSinkLogic;

                public ConsumerInfo(PartitionSinkLogic partitionSinkLogic, ImmutableList<Consumer> consumers)
                {
                    _partitionSinkLogic = partitionSinkLogic;
                    Consumers = consumers;
                    ConsumerIds = Consumers.Select(c => c.Id).ToImmutableArray();
                    Size = consumers.Count;
                }

                public ImmutableArray<long> ConsumerIds { get; }

                public long ConsumerByIndex(int index) => Consumers[index].Id;

                public int QueueSize(long consumerId) => _partitionSinkLogic._queue.Size(consumerId);

                public int Size { get; }

                public ImmutableList<Consumer> Consumers { get; }
            }

            private readonly PartitionHub<T> _hub;
            private readonly int _demandThreshold;
            private readonly Func<PartitionHub.IConsumerInfo, T, long> _materializedPartitioner;
            private readonly TaskCompletionSource<Action<IHubEvent>> _callbackCompletion = new TaskCompletionSource<Action<IHubEvent>>();
            private readonly IHubState _noRegistrationsState;
            private bool _initialized;
            private readonly IPartitionQueue _queue = new PartitionQueue();
            private readonly List<T> _pending = new List<T>();
            private ConsumerInfo _consumerInfo;
            private readonly Dictionary<long, Consumer> _needWakeup = new Dictionary<long, Consumer>();
            private long _callbackCount;

            public PartitionSinkLogic(PartitionHub<T> hub) : base(hub.Shape)
            {
                _hub = hub;
                // Half of buffer size, rounded up
                _demandThreshold = hub._bufferSize / 2 + hub._bufferSize % 2;
                _materializedPartitioner = hub._partitioner();
                _noRegistrationsState = new Open(_callbackCompletion.Task, ImmutableList<Consumer>.Empty);
                _consumerInfo = new ConsumerInfo(this, ImmutableList<Consumer>.Empty);

                State = new AtomicReference<IHubState>(_noRegistrationsState);

                SetHandler(hub.In, this);
            }

            public override void PreStart()
            {
                SetKeepGoing(true);
                _callbackCompletion.SetResult(GetAsyncCallback<IHubEvent>(OnEvent));

                if (_hub._startAfterNrOfConsumers == 0)
                    Pull(_hub.In);
            }

            public override void OnPush()
            {
                Publish(Grab(_hub.In));
                if (!IsFull) Pull(_hub.In);
            }

            private bool IsFull => _queue.TotalSize + _pending.Count >= _hub._bufferSize;

            public AtomicReference<IHubState> State { get; }

            private void Publish(T element)
            {
                if (!_initialized || _consumerInfo.Consumers.Count == 0)
                {
                    // will be published when first consumers are registered
                    _pending.Add(element);
                }
                else
                {
                    var id = _materializedPartitioner(_consumerInfo, element);
                    _queue.Offer(id, element);
                    Wakeup(id);
                }
            }

            private void Wakeup(long id)
            {
                if (_needWakeup.TryGetValue(id, out var consumer))
                {
                    _needWakeup.Remove(consumer.Id);
                    consumer.Callback(PartitionHub<T>.Wakeup.Instance);
                }
            }

            public override void OnUpstreamFinish()
            {
                if (_consumerInfo.Consumers.Count == 0)
                    CompleteStage();
                else
                {
                    foreach (var consumer in _consumerInfo.Consumers)
                        Complete(consumer.Id);
                }
            }

            private void Complete(long id)
            {
                _queue.Offer(id, Completed.Instance);
                Wakeup(id);
            }

            private void TryPull()
            {
                if (_initialized && !IsClosed(_hub.In) && !HasBeenPulled(_hub.In) && !IsFull)
                    Pull(_hub.In);
            }

            private void OnEvent(IHubEvent e)
            {
                _callbackCount++;

                if (e is NeedWakeup n)
                {
                    // Also check if the consumer is now unblocked since we published an element since it went asleep.
                    if (_queue.NonEmpty(n.Consumer.Id))
                        n.Consumer.Callback(PartitionHub<T>.Wakeup.Instance);
                    else
                    {
                        _needWakeup[n.Consumer.Id] = n.Consumer;
                        TryPull();
                    }
                }
                else if (e is TryPull)
                    TryPull();
                else if (e is RegistrationPending)
                {
                    var o = (Open)State.GetAndSet(_noRegistrationsState);
                    foreach (var consumer in o.Registrations)
                    {
                        var newConsumers = _consumerInfo.Consumers.Add(consumer).Sort((c1, c2) => c1.Id.CompareTo(c2.Id));
                        _consumerInfo = new ConsumerInfo(this, newConsumers);
                        _queue.Init(consumer.Id);
                        if (newConsumers.Count >= _hub._startAfterNrOfConsumers)
                            _initialized = true;

                        consumer.Callback(Initialize.Instance);

                        if (_initialized && _pending.Count != 0)
                        {
                            foreach (var p in _pending)
                                Publish(p);

                            _pending.Clear();
                        }

                        TryPull();
                    }
                }
                else if (e is UnRegister u)
                {
                    var newConsumers = _consumerInfo.Consumers.RemoveAll(c => c.Id == u.Id);
                    _consumerInfo = new ConsumerInfo(this, newConsumers);
                    _queue.Remove(u.Id);
                    if (newConsumers.IsEmpty)
                    {
                        if (IsClosed(_hub.In))
                            CompleteStage();
                    }
                    else
                        TryPull();
                }
            }

            public override void OnUpstreamFailure(Exception e)
            {
                var failMessage = new HubCompleted(e);

                // Notify pending consumers and set tombstone
                var o = (Open)State.GetAndSet(new Closed(e));
                foreach (var consumer in o.Registrations)
                    consumer.Callback(failMessage);

                // Notify registered consumers
                foreach (var consumer in _consumerInfo.Consumers)
                    consumer.Callback(failMessage);

                FailStage(e);
            }

            public override void PostStop()
            {
                // Notify pending consumers and set tombstone

                var s = State.Value;
                if (s is Open o)
                {
                    if (State.CompareAndSet(o, new Closed(null)))
                    {
                        var completeMessage = new HubCompleted(null);
                        foreach (var consumer in o.Registrations)
                            consumer.Callback(completeMessage);
                    }
                    else
                        PostStop();
                }
                // Already closed, ignore
            }

            // Consumer API
            public object Poll(long id, Action<IHubEvent> hubCallback)
            {
                // try pull via async callback when half full
                // this is racy with other threads doing poll but doesn't matter
                if (_queue.TotalSize == _demandThreshold)
                    hubCallback(PartitionHub<T>.TryPull.Instance);

                return _queue.Poll(id);
            }
        }

        private sealed class PartitionSource : GraphStage<SourceShape<T>>
        {
            private sealed class Logic : OutGraphStageLogic
            {
                private readonly PartitionSource _source;
                private readonly long _id;
                private readonly Consumer _consumer;
                private long _callbackCount;
                private Action<IHubEvent> _hubCallback;

                public Logic(PartitionSource source) : base(source.Shape)
                {
                    _source = source;
                    _id = source._counter.IncrementAndGet();
                    var callback = GetAsyncCallback<IConsumerEvent>(OnCommand);
                    _consumer = new Consumer(_id, callback);

                    SetHandler(source._out, this);
                }

                public override void PreStart()
                {
                    void OnHubReady(Task<Action<IHubEvent>> t)
                    {
                        if (t.IsCanceled || t.IsFaulted)
                            FailStage(t.Exception);
                        else
                        {
                            _hubCallback = t.Result;
                            _hubCallback(RegistrationPending.Instance);
                            if (IsAvailable(_source._out))
                                OnPull();
                        }
                    }

                    void Register()
                    {
                        var s = _source._logic.State.Value;
                        if (s is Closed c)
                        {
                            if (c.Failure != null)
                                FailStage(c.Failure);
                            else
                                CompleteStage();
                            return;
                        }

                        var o = (Open)s;
                        var newRegistrations = o.Registrations.Add(_consumer);
                        if (_source._logic.State.CompareAndSet(o, new Open(o.CallbackTask, newRegistrations)))
                        {
                            var callback = GetAsyncCallback<Task<Action<IHubEvent>>>(OnHubReady);
                            o.CallbackTask.ContinueWith(callback);
                        }
                        else Register();
                    }

                    Register();
                }

                public override void OnPull()
                {
                    if (_hubCallback == null) return;

                    var element = _source._logic.Poll(_id, _hubCallback);
                    if (element == null)
                        _hubCallback(new NeedWakeup(_consumer));
                    else if (element is Completed)
                        CompleteStage();
                    else
                        Push(_source._out, (T)element);
                }

                public override void PostStop() => _hubCallback?.Invoke(new UnRegister(_id));

                private void OnCommand(IConsumerEvent command)
                {
                    _callbackCount++;
                    switch (command)
                    {
                        case HubCompleted c when c.Failure != null:
                            FailStage(c.Failure);
                            break;
                        case HubCompleted _:
                            CompleteStage();
                            break;
                        case Wakeup _:
                            if (IsAvailable(_source._out))
                                OnPull();
                            break;
                        case Initialize _:
                            if (IsAvailable(_source._out) && _hubCallback != null)
                                OnPull();
                            break;
                    }
                }
            }

            private readonly AtomicCounterLong _counter;
            private readonly PartitionSinkLogic _logic;
            private readonly Outlet<T> _out = new Outlet<T>("PartitionHub.out");

            public PartitionSource(AtomicCounterLong counter, PartitionSinkLogic logic)
            {
                _counter = counter;
                _logic = logic;
                Shape = new SourceShape<T>(_out);
            }

            public override SourceShape<T> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        private readonly Func<Func<PartitionHub.IConsumerInfo, T, long>> _partitioner;
        private readonly int _startAfterNrOfConsumers;
        private readonly int _bufferSize;

        public PartitionHub(Func<Func<PartitionHub.IConsumerInfo, T, long>> partitioner, int startAfterNrOfConsumers, int bufferSize)
        {
            _partitioner = partitioner;
            _startAfterNrOfConsumers = startAfterNrOfConsumers;
            _bufferSize = bufferSize;
            Shape = new SinkShape<T>(In);
        }

        public Inlet<T> In { get; } = new Inlet<T>("PartitionHub.in");

        public override SinkShape<T> Shape { get; }

        public override ILogicAndMaterializedValue<Source<T, NotUsed>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var idCounter = new AtomicCounterLong();
            var logic = new PartitionSinkLogic(this);
            var source = new PartitionSource(idCounter, logic);

            return new LogicAndMaterializedValue<Source<T, NotUsed>>(logic, Source.FromGraph(source));
        }
    }
}
