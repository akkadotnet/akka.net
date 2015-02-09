using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Util;
using Akka.Util.Internal;
using Google.ProtocolBuffers;

namespace Akka.Remote.Transport
{
    public class ThrottlerProvider : ITransportAdapterProvider
    {
        public Transport Create(Transport wrappedTransport, ActorSystem system)
        {
            throw new NotImplementedException();
        }
    }

    public class ThrottleTransportAdapter : ActorTransportAdapter
    {
        #region Static methods and self-contained data types

        public const string Scheme = "trttl";
        public static readonly AtomicCounter UniqueId = new AtomicCounter(0);

        public enum Direction
        {
            Send,
            Receive,
            Both
        }

        #endregion

        public ThrottleTransportAdapter(Transport wrappedTransport, ActorSystem system) : base(wrappedTransport, system)
        {
        }

// ReSharper disable once InconsistentNaming
        private static readonly SchemeAugmenter _schemeAugmenter = new SchemeAugmenter(Scheme);
        protected override SchemeAugmenter SchemeAugmenter
        {
            get { return _schemeAugmenter; }
        }

        protected override string ManagerName
        {
            get
            {
                return string.Format("throttlermanager.${0}${1}", WrappedTransport.SchemeIdentifier, UniqueId.GetAndIncrement());
            }
        }

        protected override Props ManagerProps
        {
            get { throw new NotImplementedException(); }
        }
    }

    /// <summary>
    /// Management command to force disassociation of an address
    /// </summary>
    public sealed class ForceDisassociate
    {
        public ForceDisassociate(Address address)
        {
            Address = address;
        }

        public Address Address { get; private set; }
    }

    /// <summary>
    /// Management command to force disassociation of an address with an explicit error.
    /// </summary>
    public sealed class ForceDisassociateExplicitly
    {
        public ForceDisassociateExplicitly(Address address, DisassociateInfo reason)
        {
            Reason = reason;
            Address = address;
        }

        public Address Address { get; private set; }

        public DisassociateInfo Reason { get; private set; }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class ForceDisassociateAck
    {
        private ForceDisassociateAck() { }
// ReSharper disable once InconsistentNaming
        private static readonly ForceDisassociateAck _instance = new ForceDisassociateAck();

        public static ForceDisassociateAck Instance
        {
            get
            {
                return _instance;
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class ThrottlerManager : ActorTransportAdapterManager
    {
        #region Internal message classes

        internal sealed class Checkin : NoSerializationVerificationNeeded
        {
            public Checkin(Address origin, ThrottlerHandle handle)
            {
                ThrottlerHandle = handle;
                Origin = origin;
            }

            public Address Origin { get; private set; }

            public ThrottlerHandle ThrottlerHandle { get; private set; }
        }

        internal sealed class AssociateResult : NoSerializationVerificationNeeded
        {
            public AssociateResult(AssociationHandle associationHandle, TaskCompletionSource<AssociationHandle> statusPromise)
            {
                StatusPromise = statusPromise;
                AssociationHandle = associationHandle;
            }

            public AssociationHandle AssociationHandle { get; private set; }

            public TaskCompletionSource<AssociationHandle> StatusPromise { get; private set; }
        }

        internal sealed class ListenerAndMode : NoSerializationVerificationNeeded
        {
            public ListenerAndMode(IHandleEventListener handleEventListener, ThrottleMode mode)
            {
                Mode = mode;
                HandleEventListener = handleEventListener;
            }

            public IHandleEventListener HandleEventListener { get; private set; }

            public ThrottleMode Mode { get; private set; }
        }

        internal sealed class Handle : NoSerializationVerificationNeeded
        {
            public Handle(ThrottlerHandle throttlerHandle)
            {
                ThrottlerHandle = throttlerHandle;
            }

            public ThrottlerHandle ThrottlerHandle { get; private set; }
        }

        internal sealed class Listener : NoSerializationVerificationNeeded
        {
            public Listener(IHandleEventListener handleEventListener)
            {
                HandleEventListener = handleEventListener;
            }

            public IHandleEventListener HandleEventListener { get; private set; }
        }

        #endregion

        protected readonly Transport WrappedTransport;
        private Dictionary<Address, Tuple<ThrottleMode, ThrottleTransportAdapter.Direction>> _throttlingModes 
            = new Dictionary<Address, Tuple<ThrottleMode, ThrottleTransportAdapter.Direction>>();
        
        private List<Tuple<Address, ThrottlerHandle>> _handleTable = new List<Tuple<Address, ThrottlerHandle>>();

        public ThrottlerManager(Transport wrappedTransport)
        {
            WrappedTransport = wrappedTransport;
        }

        protected override void Ready(object message)
        {
            if (message is InboundAssociation)
            {
                var ia = message as InboundAssociation;
                //TODO finish
                throw new NotImplementedException();
            }
        }

        #region ThrottlerManager internal methods

        private static Address NakedAddress(Address address)
        {
            return address.Copy(protocol: string.Empty, system: string.Empty);
        }

        private Task<SetThrottleAck> AskModeWithDeathCompletion(ActorRef target, ThrottleMode mode)
        {
            if (target.IsNobody()) return Task.FromResult(SetThrottleAck.Instance);
            else
            {
                var internalTarget = target.AsInstanceOf<InternalActorRef>();
                //TODO finish
                throw new NotImplementedException();
            }
        }

        private ThrottlerHandle WrapHandle(AssociationHandle originalHandle, IAssociationEventListener listener,
            bool inbound)
        {
            var managerRef = Self;
            return new ThrottlerHandle(originalHandle, Context.ActorOf(
                RARP.For(Context.System).ConfigureDispatcher(
                Props.Create(() => new ThrottledAssociation(managerRef, listener, originalHandle, inbound)).WithDeploy(Deploy.Local)),
                "throttler" + nextId()));
        }

        #endregion
    }

    public abstract class ThrottleMode : NoSerializationVerificationNeeded
    {
        public abstract Tuple<ThrottleMode, bool> TryConsumeTokens(long nanoTimeOfSend, int tokens);
        public abstract TimeSpan TimeToAvailable(long currentNanoTime, int tokens);
    }

    public class Blackhole : ThrottleMode
    {
        private Blackhole() { }
// ReSharper disable once InconsistentNaming
        private static readonly Blackhole _instance = new Blackhole();

        public static Blackhole Instance
        {
            get
            {
                return _instance;
            }
        }

        public override Tuple<ThrottleMode, bool> TryConsumeTokens(long nanoTimeOfSend, int tokens)
        {
            return Tuple.Create<ThrottleMode, bool>(this, true);
        }

        public override TimeSpan TimeToAvailable(long currentNanoTime, int tokens)
        {
            return TimeSpan.Zero;
        }
    }

    public class Unthrottled : ThrottleMode
    {
        private Unthrottled() { }
        private static readonly Unthrottled _instance = new Unthrottled();

        public static Unthrottled Instance
        {
            get
            {
                return _instance;
            }
        }

        public override Tuple<ThrottleMode, bool> TryConsumeTokens(long nanoTimeOfSend, int tokens)
        {
            return Tuple.Create<ThrottleMode, bool>(this, false);
        }

        public override TimeSpan TimeToAvailable(long currentNanoTime, int tokens)
        {
            return TimeSpan.Zero;
        }
    }

    sealed class TokenBucket : ThrottleMode
    {
        readonly int _capacity;
        readonly double _tokensPerSecond;
        readonly long _nanoTimeOfLastSend;
        readonly int _availableTokens;

        public TokenBucket(int capacity, double tokensPerSecond, long nanoTimeOfLastSend, int availableTokens)
        {
            _capacity = capacity;
            _tokensPerSecond = tokensPerSecond;
            _nanoTimeOfLastSend = nanoTimeOfLastSend;
            _availableTokens = availableTokens;
        }

        bool IsAvailable(long nanoTimeOfSend, int tokens)
        {
            if (tokens > _capacity && _availableTokens > 0)
                return true; // Allow messages larger than capacity through, it will be recorded as negative tokens
            return Math.Min(_availableTokens + TokensGenerated(nanoTimeOfSend), _capacity) >= tokens;
        }

        public override Tuple<ThrottleMode, bool> TryConsumeTokens(long nanoTimeOfSend, int tokens)
        {
            if (IsAvailable(nanoTimeOfSend, tokens))
            {
                return Tuple.Create<ThrottleMode, bool>(Copy(
                    nanoTimeOfLastSend: nanoTimeOfSend,
                    availableTokens: Math.Min(_availableTokens - tokens + TokensGenerated(nanoTimeOfSend), _capacity))
                    , true);
            }
            return Tuple.Create<ThrottleMode, bool>(this, false);
        }

        public override TimeSpan TimeToAvailable(long currentNanoTime, int tokens)
        {
            var needed = (tokens > _capacity ? 1 : tokens) - TokensGenerated(currentNanoTime);
            return TimeSpan.FromSeconds(needed / _tokensPerSecond);
        }

        int TokensGenerated(long nanoTimeOfSend)
        {
            return Convert.ToInt32((nanoTimeOfSend - nanoTimeOfSend) * 1000000 * _tokensPerSecond / 1000);
        }

        TokenBucket Copy(int? capacity = null, double? tokensPerSecond = null, long? nanoTimeOfLastSend = null, int? availableTokens = null)
        {
            return new TokenBucket(
                capacity ?? _capacity,
                tokensPerSecond ?? _tokensPerSecond,
                nanoTimeOfLastSend ?? _nanoTimeOfLastSend,
                availableTokens ?? _availableTokens);
        }

        private bool Equals(TokenBucket other)
        {
            return _capacity == other._capacity
                && _tokensPerSecond.Equals(other._tokensPerSecond)
                && _nanoTimeOfLastSend == other._nanoTimeOfLastSend
                && _availableTokens == other._availableTokens;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is TokenBucket && Equals((TokenBucket)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _capacity;
                hashCode = (hashCode * 397) ^ _tokensPerSecond.GetHashCode();
                hashCode = (hashCode * 397) ^ _nanoTimeOfLastSend.GetHashCode();
                hashCode = (hashCode * 397) ^ _availableTokens;
                return hashCode;
            }
        }

        public static bool operator ==(TokenBucket left, TokenBucket right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(TokenBucket left, TokenBucket right)
        {
            return !Equals(left, right);
        }
    }

    internal sealed class SetThrottle
    {
        readonly Address _address;
        public Address Address { get { return _address; } }
        readonly ThrottleTransportAdapter.Direction _direction;
        public ThrottleTransportAdapter.Direction Direction { get { return _direction; } }
        readonly ThrottleMode _mode;
        public ThrottleMode Mode { get { return _mode; } }

        public SetThrottle(Address address, ThrottleTransportAdapter.Direction direction, ThrottleMode mode)
        {
            _address = address;
            _direction = direction;
            _mode = mode;
        }

        private bool Equals(SetThrottle other)
        {
            return Equals(_address, other._address) && _direction == other._direction && Equals(_mode, other._mode);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is SetThrottle && Equals((SetThrottle)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (_address != null ? _address.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (int)_direction;
                hashCode = (hashCode * 397) ^ (_mode != null ? _mode.GetHashCode() : 0);
                return hashCode;
            }
        }

        public static bool operator ==(SetThrottle left, SetThrottle right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(SetThrottle left, SetThrottle right)
        {
            return !Equals(left, right);
        }
    }

    internal sealed class SetThrottleAck
    {
        private SetThrottleAck() { }
// ReSharper disable once InconsistentNaming
        private static readonly SetThrottleAck _instance = new SetThrottleAck();

        public static SetThrottleAck Instance
        {
            get { return _instance; }
        }
    }
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ThrottlerHandle : AbstractTransportAdapterHandle
    {
        private readonly ActorRef _throttlerActor;
        private AtomicReference<ThrottleMode> _outboundThrottleMode = new AtomicReference<ThrottleMode>(Unthrottled.Instance);

        

        public ThrottlerHandle(AssociationHandle wrappedHandle, ActorRef throttlerActor) : base(wrappedHandle, ThrottleTransportAdapter.Scheme)
        {
            _throttlerActor = throttlerActor;
            ReadHandlerSource = new TaskCompletionSource<IHandleEventListener>();
        }

        public override bool Write(ByteString payload)
        {
            var tokens = payload.Length;
            //need to declare recursive delegates first before they can self-reference
            //might want to consider making this consumer function strongly typed: http://blogs.msdn.com/b/wesdyer/archive/2007/02/02/anonymous-recursion-in-c.aspx
            Func<ThrottleMode, bool> tryConsume = null; 
            tryConsume = currentBucket =>
            {
                var timeOfSend = SystemNanoTime.GetNanos();
                var res = currentBucket.TryConsumeTokens(timeOfSend, tokens);
                var newBucket = res.Item1;
                var allow = res.Item2;
                if (allow)
                {
                    return _outboundThrottleMode.CompareAndSet(currentBucket, newBucket) || tryConsume(_outboundThrottleMode.Value);
                }
                return false;
            };

            var throttleMode = _outboundThrottleMode.Value;
            if (throttleMode is Blackhole) return true;
            
            var sucess = tryConsume(_outboundThrottleMode.Value);
            return sucess && WrappedHandle.Write(payload);
        }

        public override void Disassociate()
        {
            _throttlerActor.Tell(PoisonPill.Instance);
        }

        public void DisassociateWithFailure(DisassociateInfo reason)
        {
            _throttlerActor.Tell(new ThrottledAssociation.FailWith(reason));
        }
    }

    internal static class SystemNanoTime
    {
        /// <summary>
        /// Need to have time that is much more precise than <see cref="DateTime.Now"/> when throttling sends
        /// </summary>
        private static readonly Stopwatch StopWatch;

        static SystemNanoTime()
        {
            StopWatch = new Stopwatch();
            StopWatch.Start();
        }

        public static long GetNanos()
        {
            return StopWatch.ElapsedTicks;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class ThrottledAssociation : FSM<ThrottledAssociation.ThrottlerState, ThrottledAssociation.IThrottlerData>, LoggingFSM
    {
        #region ThrottledAssociation FSM state and data classes

        private const string DequeueTimerName = "dequeue";

        sealed class Dequeue { }
        
        public enum ThrottlerState
        {
            /*
             * STATES FOR INBOUND ASSOCIATIONS
             */

            /// <summary>
            /// Waiting for the <see cref="ThrottlerHandle"/> coupled with the throttler actor.
            /// </summary>
            WaitExposedHandle,
            /// <summary>
            /// Waiting for the ASSOCIATE message that contains the origin address of the remote endpoint
            /// </summary>
            WaitOrigin,
            /// <summary>
            /// After origin is known and a Checkin message is sent to the manager, we must wait for the <see cref="ThrottleMode"/>
            /// for the address
            /// </summary>
            WaitMode,
            /// <summary>
            /// After all information is known, the throttler must wait for the upstream listener to be able to forward messages
            /// </summary>
            WaitUpstreamListener,

            /*
             * STATES FOR OUTBOUND ASSOCIATIONS
             */
            /// <summary>
            /// Waiting for the tuple containing the upstream listener and the <see cref="ThrottleMode"/>
            /// </summary>
            WaitModeAndUpstreamListener,
            /// <summary>
            /// Fully initialized state
            /// </summary>
            Throttling
        }

        internal interface IThrottlerData { }

        internal class Uninitialized : IThrottlerData
        {
            private Uninitialized() { }
// ReSharper disable once InconsistentNaming
            private static readonly Uninitialized _instance = new Uninitialized();
            public static Uninitialized Instance { get { return _instance; } }
        }

        internal sealed class ExposedHandle : IThrottlerData
        {
            public ExposedHandle(ThrottlerHandle handle)
            {
                Handle = handle;
            }

            public ThrottlerHandle Handle { get; private set; }
        }

        internal sealed class FailWith
        {
            public FailWith(DisassociateInfo failReason)
            {
                FailReason = failReason;
            }

            public DisassociateInfo FailReason { get; private set; }
        }

        #endregion

        protected ActorRef Manager;
        protected IAssociationEventListener AssociationHandler;
        protected AssociationHandle OriginalHandle;
        protected bool Inbound;

        protected ThrottleMode InboundThrottleMode;
        protected Queue<ByteString> ThrottledMessages = new Queue<ByteString>();
        protected IHandleEventListener UpstreamListener;

        /// <summary>
        /// Used for decoding certain types of throttled messages on-the-fly
        /// </summary>
        private static readonly AkkaPduProtobuffCodec Codec = new AkkaPduProtobuffCodec();

        public ThrottledAssociation(ActorRef manager, IAssociationEventListener associationHandler, AssociationHandle originalHandle, bool inbound)
        {
            Manager = manager;
            AssociationHandler = associationHandler;
            OriginalHandle = originalHandle;
            Inbound = inbound;
            InitializeFSM();
        }

        private void InitializeFSM()
        {
            When(ThrottlerState.WaitExposedHandle, @event =>
            {
                if (@event.FsmEvent is ThrottlerManager.Handle && @event.StateData is Uninitialized)
                {
                    // register to downstream layer and wait for origin
                    OriginalHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(Self));
                    return
                        GoTo(ThrottlerState.WaitOrigin)
                            .Using(
                                new ExposedHandle(
                                    @event.FsmEvent.AsInstanceOf<ThrottlerManager.Handle>().ThrottlerHandle));
                }
                return null;
            });

            When(ThrottlerState.WaitOrigin, @event =>
            {
                if (@event.FsmEvent is InboundPayload && @event.StateData is ExposedHandle)
                {
                    var b = @event.FsmEvent.AsInstanceOf<InboundPayload>().Payload;
                    ThrottledMessages.Enqueue(b);
                    var origin = PeekOrigin(b);
                    if (origin != null)
                    {
                        Manager.Tell(new ThrottlerManager.Checkin(origin, @event.StateData.AsInstanceOf<ExposedHandle>().Handle));
                        return GoTo(ThrottlerState.WaitMode);
                    }
                    return Stay();
                }
                return null;
            });

            When(ThrottlerState.WaitMode, @event =>
            {
                if (@event.FsmEvent is InboundPayload)
                {
                    var b = @event.FsmEvent.AsInstanceOf<InboundPayload>().Payload;
                    ThrottledMessages.Enqueue(b);
                    return Stay();
                }

                if (@event.FsmEvent is ThrottleMode && @event.StateData is ExposedHandle)
                {
                    var mode =  @event.FsmEvent.AsInstanceOf<ThrottleMode>();
                    var exposedHandle = @event.StateData.AsInstanceOf<ExposedHandle>().Handle;
                    InboundThrottleMode = mode;
                    try
                    {
                        if (mode is Blackhole)
                        {
                            ThrottledMessages = new Queue<ByteString>();
                            exposedHandle.Disassociate();
                            return Stop();
                        }
                        else
                        {
                            AssociationHandler.Notify(new InboundAssociation(exposedHandle));
                            exposedHandle.ReadHandlerSource.Task.ContinueWith(
                                r => new ThrottlerManager.Listener(r.Result),
                                TaskContinuationOptions.AttachedToParent & TaskContinuationOptions.ExecuteSynchronously)
                                .PipeTo(Self);
                            return GoTo(ThrottlerState.WaitUpstreamListener);
                        }
                    }
                    finally
                    {
                        Sender.Tell(SetThrottleAck.Instance);
                    }
                }

                return null;
            });

            When(ThrottlerState.WaitUpstreamListener, @event =>
            {
                if (@event.FsmEvent is InboundPayload)
                {
                    var b = @event.FsmEvent.AsInstanceOf<InboundPayload>();
                    ThrottledMessages.Enqueue(b.Payload);
                    return Stay();
                }

                if (@event.FsmEvent is ThrottlerManager.Listener)
                {
                    UpstreamListener = @event.FsmEvent.AsInstanceOf<ThrottlerManager.Listener>().HandleEventListener;
                    Self.Tell(new Dequeue());
                    return GoTo(ThrottlerState.Throttling);
                }

                return null;
            });

            When(ThrottlerState.WaitModeAndUpstreamListener, @event =>
            {
                if (@event.FsmEvent is ThrottlerManager.ListenerAndMode)
                {
                    var listenerAndMode = @event.FsmEvent.AsInstanceOf<ThrottlerManager.ListenerAndMode>();
                    UpstreamListener = listenerAndMode.HandleEventListener;
                    InboundThrottleMode = listenerAndMode.Mode;
                    Self.Tell(new Dequeue());
                    return GoTo(ThrottlerState.Throttling);
                }

                if (@event.FsmEvent is InboundPayload)
                {
                    var b = @event.FsmEvent.AsInstanceOf<InboundPayload>();
                    ThrottledMessages.Enqueue(b.Payload);
                    return Stay();
                }

                return null;
            });

            When(ThrottlerState.Throttling, @event =>
            {
                if (@event.FsmEvent is ThrottleMode)
                {
                    var mode = @event.FsmEvent.AsInstanceOf<ThrottleMode>();
                    InboundThrottleMode = mode;
                    if(mode is Blackhole) ThrottledMessages = new Queue<ByteString>();
                    CancelTimer(DequeueTimerName);
                    if(!ThrottledMessages.Any())
                        ScheduleDequeue(InboundThrottleMode.TimeToAvailable(SystemNanoTime.GetNanos(), ThrottledMessages.Peek().Length));
                    Sender.Tell(SetThrottleAck.Instance);
                    return Stay();
                }

                if (@event.FsmEvent is InboundPayload)
                {
                    ForwardOrDelay(@event.FsmEvent.AsInstanceOf<InboundPayload>().Payload);
                    return Stay();
                }

                if (@event.FsmEvent is Dequeue)
                {
                    if (ThrottledMessages.Any())
                    {
                        var payload = ThrottledMessages.Dequeue();
                        UpstreamListener.Notify(new InboundPayload(payload));
                        InboundThrottleMode = InboundThrottleMode.TryConsumeTokens(SystemNanoTime.GetNanos(),
                            payload.Length).Item1;
                        if(ThrottledMessages.Any())
                            ScheduleDequeue(InboundThrottleMode.TimeToAvailable(SystemNanoTime.GetNanos(), ThrottledMessages.Peek().Length));
                    }
                    return Stay();
                }

                return null;
            });

            WhenUnhandled(@event =>
            {
                // we should always set the throttling mode
                if (@event.FsmEvent is ThrottleMode)
                {
                    InboundThrottleMode = @event.FsmEvent.AsInstanceOf<ThrottleMode>();
                    Sender.Tell(SetThrottleAck.Instance);
                    return Stay();
                }

                if (@event.FsmEvent is Disassociated)
                {
                    return Stop(); // not notifying the upstream handler is intentional: we are relying on heartbeating
                }

                if (@event.FsmEvent is FailWith)
                {
                    var reason = @event.FsmEvent.AsInstanceOf<FailWith>().FailReason;
                    if(UpstreamListener != null) UpstreamListener.Notify(new Disassociated(reason));
                    return Stop();
                }

                return null;
            });

            if(Inbound)
                StartWith(ThrottlerState.WaitExposedHandle, Uninitialized.Instance);
            else
            {
                OriginalHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(Self));
                StartWith(ThrottlerState.WaitModeAndUpstreamListener, Uninitialized.Instance);
            }
        }

        /// <summary>
        /// This method captures ASSOCIATE packets and extracts the origin <see cref="Address"/>.
        /// </summary>
        /// <param name="b">Inbound <see cref="ByteString"/> received from network.</param>
        /// <returns></returns>
        private Address PeekOrigin(ByteString b)
        {
            try
            {
                var pdu = Codec.DecodePdu(b);
                if (pdu is Associate)
                {
                    return pdu.AsInstanceOf<Associate>().Info.Origin;
                }
                return null;
            }
            catch
            {
                // This layer should not care about malformed packets. Also, this also useful for testing, because
                // arbitrary payload could be passed in
                return null;
            }
        }

        private void ScheduleDequeue(TimeSpan delay)
        {
            if (InboundThrottleMode is Blackhole) return; //do nothing
            if (delay <= TimeSpan.Zero) Self.Tell(new Dequeue());
            else
            {
                SetTimer(DequeueTimerName, new Dequeue(), delay, repeat:false);
            }
        }

        private void ForwardOrDelay(ByteString payload)
        {
            if (InboundThrottleMode is Blackhole)
            {
                // Do nothing
            }
            else
            {
                if (!ThrottledMessages.Any())
                {
                    var tokens = payload.Length;
                    var res = InboundThrottleMode.TryConsumeTokens(SystemNanoTime.GetNanos(), tokens);
                    var newBucket = res.Item1;
                    var success = res.Item2;
                    if (success)
                    {
                        InboundThrottleMode = newBucket;
                        UpstreamListener.Notify(new InboundPayload(payload));
                    }
                    else
                    {
                        ThrottledMessages.Enqueue(payload);
                        ScheduleDequeue(InboundThrottleMode.TimeToAvailable(SystemNanoTime.GetNanos(), tokens));
                    }
                }
                else
                {
                    ThrottledMessages.Enqueue(payload);
                }
            }
        }
    }
}
