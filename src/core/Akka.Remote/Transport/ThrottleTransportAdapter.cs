//-----------------------------------------------------------------------
// <copyright file="ThrottleTransportAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Remote.Serialization;
using Akka.Util;
using Akka.Util.Internal;
using Google.Protobuf;

namespace Akka.Remote.Transport
{
    /// <summary>
    /// Used to provide throttling controls for remote <see cref="Transport"/> instances.
    /// </summary>
    public class ThrottlerProvider : ITransportAdapterProvider
    {
        /// <inheritdoc cref="ITransportAdapterProvider"/>
        public Transport Create(Transport wrappedTransport, ExtendedActorSystem system)
        {
            return new ThrottleTransportAdapter(wrappedTransport, system);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// The throttler transport adapter
    /// </summary>
    public class ThrottleTransportAdapter : ActorTransportAdapter
    {
        #region Static methods and self-contained data types

        /// <summary>
        /// TBD
        /// </summary>
        public const string Scheme = "trttl";
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly AtomicCounter UniqueId = new AtomicCounter(0);

        /// <summary>
        /// TBD
        /// </summary>
        public enum Direction
        {
            /// <summary>
            /// TBD
            /// </summary>
            Send,
            /// <summary>
            /// TBD
            /// </summary>
            Receive,
            /// <summary>
            /// TBD
            /// </summary>
            Both
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="wrappedTransport">TBD</param>
        /// <param name="system">TBD</param>
        public ThrottleTransportAdapter(Transport wrappedTransport, ActorSystem system) : base(wrappedTransport, system)
        {
        }

        // ReSharper disable once InconsistentNaming
        private static readonly SchemeAugmenter _schemeAugmenter = new SchemeAugmenter(Scheme);
        /// <summary>
        /// TBD
        /// </summary>
        protected override SchemeAugmenter SchemeAugmenter
        {
            get { return _schemeAugmenter; }
        }

        /// <summary>
        /// The name of the actor managing the throttler
        /// </summary>
        protected override string ManagerName
        {
            get
            {
                return string.Format("throttlermanager.${0}${1}", WrappedTransport.SchemeIdentifier, UniqueId.GetAndIncrement());
            }
        }

        /// <summary>
        /// The props for starting the <see cref="ThrottlerManager"/>
        /// </summary>
        protected override Props ManagerProps
        {
            get
            {
                var wt = WrappedTransport;
                return Props.Create(() => new ThrottlerManager(wt));
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        public override Task<bool> ManagementCommand(object message)
        {
            if (message is SetThrottle)
            {
                return manager.Ask(message, AskTimeout).ContinueWith(r =>
                {
                    return r.Result is SetThrottleAck;
                });
            }

            if (message is ForceDisassociate || message is ForceDisassociateExplicitly)
            {
                return manager.Ask(message, AskTimeout).ContinueWith(r => r.Result is ForceDisassociateAck,
                    TaskContinuationOptions.ExecuteSynchronously);
            }

            return WrappedTransport.ManagementCommand(message);
        }
    }

    /// <summary>
    /// Management command to force disassociation of an address
    /// </summary>
    internal sealed class ForceDisassociate
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="address">TBD</param>
        public ForceDisassociate(Address address)
        {
            Address = address;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Address Address { get; private set; }
    }

    /// <summary>
    /// Management command to force disassociation of an address with an explicit error.
    /// </summary>
    internal sealed class ForceDisassociateExplicitly
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="address">TBD</param>
        /// <param name="reason">TBD</param>
        public ForceDisassociateExplicitly(Address address, DisassociateInfo reason)
        {
            Reason = reason;
            Address = address;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Address Address { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public DisassociateInfo Reason { get; private set; }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ForceDisassociateAck
    {
        private ForceDisassociateAck() { }
        // ReSharper disable once InconsistentNaming
        private static readonly ForceDisassociateAck _instance = new ForceDisassociateAck();

        /// <summary>
        /// TBD
        /// </summary>
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

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class Checkin : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="origin">TBD</param>
            /// <param name="handle">TBD</param>
            public Checkin(Address origin, ThrottlerHandle handle)
            {
                ThrottlerHandle = handle;
                Origin = origin;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Address Origin { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            public ThrottlerHandle ThrottlerHandle { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class AssociateResult : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="associationHandle">TBD</param>
            /// <param name="statusPromise">TBD</param>
            public AssociateResult(AssociationHandle associationHandle, TaskCompletionSource<AssociationHandle> statusPromise)
            {
                StatusPromise = statusPromise;
                AssociationHandle = associationHandle;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public AssociationHandle AssociationHandle { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            public TaskCompletionSource<AssociationHandle> StatusPromise { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class ListenerAndMode : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="handleEventListener">TBD</param>
            /// <param name="mode">TBD</param>
            public ListenerAndMode(IHandleEventListener handleEventListener, ThrottleMode mode)
            {
                Mode = mode;
                HandleEventListener = handleEventListener;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IHandleEventListener HandleEventListener { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            public ThrottleMode Mode { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class Handle : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="throttlerHandle">TBD</param>
            public Handle(ThrottlerHandle throttlerHandle)
            {
                ThrottlerHandle = throttlerHandle;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public ThrottlerHandle ThrottlerHandle { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class Listener : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="handleEventListener">TBD</param>
            public Listener(IHandleEventListener handleEventListener)
            {
                HandleEventListener = handleEventListener;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IHandleEventListener HandleEventListener { get; private set; }
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        protected readonly Transport WrappedTransport;
        private Dictionary<Address, (ThrottleMode, ThrottleTransportAdapter.Direction)> _throttlingModes
            = new Dictionary<Address, (ThrottleMode, ThrottleTransportAdapter.Direction)>();

        private List<(Address, ThrottlerHandle)> _handleTable = new List<(Address, ThrottlerHandle)>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="wrappedTransport">TBD</param>
        public ThrottlerManager(Transport wrappedTransport)
        {
            WrappedTransport = wrappedTransport;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        protected override void Ready(object message)
        {
            if (message is InboundAssociation)
            {
                var ia = message as InboundAssociation;
                var wrappedHandle = WrapHandle(ia.Association, AssociationListener, true);
                wrappedHandle.ThrottlerActor.Tell(new Handle(wrappedHandle));
            }
            else if (message is AssociateUnderlying)
            {
                var ua = message as AssociateUnderlying;

                // Slight modification of PipeTo, only success is sent, failure is propagated to a separate Task
                var associateTask = WrappedTransport.Associate(ua.RemoteAddress);
                var self = Self;
                associateTask.ContinueWith(tr =>
                {
                    if (tr.IsFaulted)
                    {
                        ua.StatusPromise.SetException(tr.Exception ?? new Exception("association failed"));
                    }
                    else
                    {
                        self.Tell(new AssociateResult(tr.Result, ua.StatusPromise));
                    }

                }, TaskContinuationOptions.ExecuteSynchronously);

            }
            else if (message is AssociateResult) // Finished outbound association and got back the handle
            {
                var ar = message as AssociateResult;
                var wrappedHandle = WrapHandle(ar.AssociationHandle, AssociationListener, false);
                var naked = NakedAddress(ar.AssociationHandle.RemoteAddress);
                var inMode = GetInboundMode(naked);
                wrappedHandle.OutboundThrottleMode.Value = GetOutboundMode(naked);
                wrappedHandle.ReadHandlerSource.Task.ContinueWith(tr => new ListenerAndMode(tr.Result, inMode), TaskContinuationOptions.ExecuteSynchronously)
                    .PipeTo(wrappedHandle.ThrottlerActor);
                _handleTable.Add((naked, wrappedHandle));
                ar.StatusPromise.SetResult(wrappedHandle);
            }
            else if (message is SetThrottle)
            {
                var st = message as SetThrottle;
                var naked = NakedAddress(st.Address);
                _throttlingModes[naked] = (st.Mode, st.Direction);
                var ok = Task.FromResult(SetThrottleAck.Instance);
                var modes = new List<Task<SetThrottleAck>>() { ok };
                foreach (var handle in _handleTable)
                {
                    if (handle.Item1 == naked)
                        modes.Add(SetMode(handle.Item2, st.Mode, st.Direction));
                }

                var sender = Sender;
                Task.WhenAll(modes).ContinueWith(tr =>
                {
                    return SetThrottleAck.Instance;
                }, TaskContinuationOptions.ExecuteSynchronously)
                    .PipeTo(sender);
            }
            else if (message is ForceDisassociate)
            {
                var fd = message as ForceDisassociate;
                var naked = NakedAddress(fd.Address);
                foreach (var handle in _handleTable)
                {
                    if (handle.Item1 == naked)
                        handle.Item2.Disassociate();
                }

                /*
                 * NOTE: Important difference between Akka.NET and Akka here.
                 * In canonical Akka, ThrottleHandlers are never removed from
                 * the _handleTable. The reason is because Ask-ing a terminated ActorRef
                 * doesn't cause any exceptions to be thrown upstream - it just times out
                 * and propagates a failed Future.
                 * 
                 * In the CLR, a CancellationException gets thrown and causes all
                 * parent tasks chaining back to the EndPointManager to fail due
                 * to an Ask timeout.
                 * 
                 * So in order to avoid this problem, we remove any disassociated handles
                 * from the _handleTable.
                 * 
                 * Questions? Ask @Aaronontheweb
                 */
                _handleTable.RemoveAll(tuple => tuple.Item1 == naked);
                Sender.Tell(ForceDisassociateAck.Instance);
            }
            else if (message is ForceDisassociateExplicitly)
            {
                var fde = message as ForceDisassociateExplicitly;
                var naked = NakedAddress(fde.Address);
                foreach (var handle in _handleTable)
                {
                    if (handle.Item1 == naked)
                        handle.Item2.DisassociateWithFailure(fde.Reason);
                }

                /*
                 * NOTE: Important difference between Akka.NET and Akka here.
                 * In canonical Akka, ThrottleHandlers are never removed from
                 * the _handleTable. The reason is because Ask-ing a terminated ActorRef
                 * doesn't cause any exceptions to be thrown upstream - it just times out
                 * and propagates a failed Future.
                 * 
                 * In the CLR, a CancellationException gets thrown and causes all
                 * parent tasks chaining back to the EndPointManager to fail due
                 * to an Ask timeout.
                 * 
                 * So in order to avoid this problem, we remove any disassociated handles
                 * from the _handleTable.
                 * 
                 * Questions? Ask @Aaronontheweb
                 */
                _handleTable.RemoveAll(tuple => tuple.Item1 == naked);
                Sender.Tell(ForceDisassociateAck.Instance);
            }
            else if (message is Checkin)
            {
                var chkin = message as Checkin;
                var naked = NakedAddress(chkin.Origin);
                _handleTable.Add((naked, chkin.ThrottlerHandle));
                SetMode(naked, chkin.ThrottlerHandle);
            }
        }

        #region ThrottlerManager internal methods

        private static Address NakedAddress(Address address)
        {
            return address.WithProtocol(string.Empty)
                .WithSystem(string.Empty);
        }

        private ThrottleMode GetInboundMode(Address nakedAddress)
        {
            if (_throttlingModes.TryGetValue(nakedAddress, out var mode))
                if (mode.Item2 == ThrottleTransportAdapter.Direction.Both || mode.Item2 == ThrottleTransportAdapter.Direction.Receive)
                    return mode.Item1;

            return Unthrottled.Instance;
        }

        private ThrottleMode GetOutboundMode(Address nakedAddress)
        {
            if (_throttlingModes.TryGetValue(nakedAddress, out var mode))
                if (mode.Item2 == ThrottleTransportAdapter.Direction.Both || mode.Item2 == ThrottleTransportAdapter.Direction.Send)
                    return mode.Item1;

            return Unthrottled.Instance;
        }

        private Task<SetThrottleAck> SetMode(Address nakedAddress, ThrottlerHandle handle)
        {
            if (_throttlingModes.TryGetValue(nakedAddress, out var mode))
                return SetMode(handle, mode.Item1, mode.Item2);

            return SetMode(handle, Unthrottled.Instance, ThrottleTransportAdapter.Direction.Both);
        }

        private Task<SetThrottleAck> SetMode(ThrottlerHandle handle, ThrottleMode mode,
            ThrottleTransportAdapter.Direction direction)
        {
            if (direction == ThrottleTransportAdapter.Direction.Both ||
                direction == ThrottleTransportAdapter.Direction.Send)
                handle.OutboundThrottleMode.Value = mode;
            if (direction == ThrottleTransportAdapter.Direction.Both ||
                direction == ThrottleTransportAdapter.Direction.Receive)
                return AskModeWithDeathCompletion(handle.ThrottlerActor, mode, ActorTransportAdapter.AskTimeout);
            else
                return Task.FromResult(SetThrottleAck.Instance);
        }

        private Task<SetThrottleAck> AskModeWithDeathCompletion(IActorRef target, ThrottleMode mode, TimeSpan timeout)
        {
            if (target.IsNobody()) return Task.FromResult(SetThrottleAck.Instance);


            var internalTarget = target.AsInstanceOf<IInternalActorRef>();
            var promiseRef = PromiseActorRef.Apply(internalTarget.Provider, timeout, target, mode.GetType().Name);
            internalTarget.SendSystemMessage(new Watch(internalTarget, promiseRef));
            target.Tell(mode, promiseRef);
            return promiseRef.Result.ContinueWith(tr =>
            {
                var t = tr.Result as Terminated;
                if (t != null && t.ActorRef.Path.Equals(target.Path))
                    return SetThrottleAck.Instance;
                internalTarget.SendSystemMessage(new Unwatch(internalTarget, promiseRef));
                return SetThrottleAck.Instance;

            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private ThrottlerHandle WrapHandle(AssociationHandle originalHandle, IAssociationEventListener listener,
            bool inbound)
        {
            var managerRef = Self;
            return new ThrottlerHandle(originalHandle, Context.ActorOf(
                RARP.For(Context.System).ConfigureDispatcher(
                Props.Create(() => new ThrottledAssociation(managerRef, listener, originalHandle, inbound)).WithDeploy(Deploy.Local)),
                "throttler" + NextId()));
        }

        #endregion
    }

    /// <summary>
    /// The type of throttle being applied to a connection.
    /// </summary>
    public abstract class ThrottleMode : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="nanoTimeOfSend">TBD</param>
        /// <param name="tokens">TBD</param>
        /// <returns>TBD</returns>
        public abstract (ThrottleMode, bool) TryConsumeTokens(long nanoTimeOfSend, int tokens);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="currentNanoTime">TBD</param>
        /// <param name="tokens">TBD</param>
        /// <returns>TBD</returns>
        public abstract TimeSpan TimeToAvailable(long currentNanoTime, int tokens);
    }

    /// <summary>
    /// Signals that we're going to totally black out a connection
    /// </summary>
    public class Blackhole : ThrottleMode
    {
        private Blackhole() { }
        // ReSharper disable once InconsistentNaming
        private static readonly Blackhole _instance = new Blackhole();

        /// <summary>
        /// The singleton instance
        /// </summary>
        public static Blackhole Instance
        {
            get
            {
                return _instance;
            }
        }

        /// <inheritdoc/>
        public override (ThrottleMode, bool) TryConsumeTokens(long nanoTimeOfSend, int tokens)
        {
            return (this, false);
        }

        /// <inheritdoc/>
        public override TimeSpan TimeToAvailable(long currentNanoTime, int tokens)
        {
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Unthrottles a previously throttled connection
    /// </summary>
    public class Unthrottled : ThrottleMode
    {
        private Unthrottled() { }
        private static readonly Unthrottled _instance = new Unthrottled();

        /// <summary>
        /// TBD
        /// </summary>
        public static Unthrottled Instance
        {
            get
            {
                return _instance;
            }
        }

        /// <inheritdoc/>
        public override (ThrottleMode, bool) TryConsumeTokens(long nanoTimeOfSend, int tokens)
        {
            return (this, true);
        }

        /// <inheritdoc/>
        public override TimeSpan TimeToAvailable(long currentNanoTime, int tokens)
        {
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Applies token-bucket throttling to introduce latency to a connection
    /// </summary>
    sealed class TokenBucket : ThrottleMode
    {
        readonly int _capacity;
        readonly double _tokensPerSecond;
        readonly long _nanoTimeOfLastSend;
        readonly int _availableTokens;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="capacity">TBD</param>
        /// <param name="tokensPerSecond">TBD</param>
        /// <param name="nanoTimeOfLastSend">TBD</param>
        /// <param name="availableTokens">TBD</param>
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

        /// <inheritdoc/>
        public override (ThrottleMode, bool) TryConsumeTokens(long nanoTimeOfSend, int tokens)
        {
            if (IsAvailable(nanoTimeOfSend, tokens))
            {
                return (Copy(
                        nanoTimeOfLastSend: nanoTimeOfSend,
                        availableTokens: Math.Min(_availableTokens - tokens + TokensGenerated(nanoTimeOfSend), _capacity))
                    , true);
            }
            return (this, false);
        }

        /// <inheritdoc/>
        public override TimeSpan TimeToAvailable(long currentNanoTime, int tokens)
        {
            var needed = (tokens > _capacity ? 1 : tokens) - TokensGenerated(currentNanoTime);
            return TimeSpan.FromSeconds(needed / _tokensPerSecond);
        }

        int TokensGenerated(long nanoTimeOfSend)
        {
            var milliSecondsSinceLastSend = ((nanoTimeOfSend - _nanoTimeOfLastSend).ToTicks() / TimeSpan.TicksPerMillisecond);
            var tokensGenerated = milliSecondsSinceLastSend * _tokensPerSecond / 1000;
            return Convert.ToInt32(tokensGenerated);
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

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is TokenBucket && Equals((TokenBucket)obj);
        }

        /// <inheritdoc/>
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

        /// <summary>
        /// Compares two specified <see cref="TokenBucket"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="TokenBucket"/> used for comparison</param>
        /// <param name="right">The second <see cref="TokenBucket"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="TokenBucket">TokenBuckets</see> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(TokenBucket left, TokenBucket right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="TokenBucket"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="TokenBucket"/> used for comparison</param>
        /// <param name="right">The second <see cref="TokenBucket"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="TokenBucket">TokenBuckets</see> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(TokenBucket left, TokenBucket right)
        {
            return !Equals(left, right);
        }
    }

    /// <summary>
    /// Applies a throttle to the underlying conneciton
    /// </summary>
    public sealed class SetThrottle
    {
        readonly Address _address;
        /// <summary>
        /// The address of the remote node we'll be throttling
        /// </summary>
        public Address Address { get { return _address; } }

        readonly ThrottleTransportAdapter.Direction _direction;
        /// <summary>
        /// The direction of the throttle
        /// </summary>
        public ThrottleTransportAdapter.Direction Direction { get { return _direction; } }
        readonly ThrottleMode _mode;
        /// <summary>
        /// The mode of the throttle
        /// </summary>
        public ThrottleMode Mode { get { return _mode; } }

        /// <summary>
        /// Creates a new SetThrottle message.
        /// </summary>
        /// <param name="address">The address of the throttle.</param>
        /// <param name="direction">The direction of the throttle.</param>
        /// <param name="mode">The mode of the throttle.</param>
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

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is SetThrottle && Equals((SetThrottle)obj);
        }

        /// <inheritdoc/>
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

        /// <summary>
        /// Compares two specified <see cref="SetThrottle"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="SetThrottle"/> used for comparison</param>
        /// <param name="right">The second <see cref="SetThrottle"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="SetThrottle">SetThrottles</see> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(SetThrottle left, SetThrottle right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="SetThrottle"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="SetThrottle"/> used for comparison</param>
        /// <param name="right">The second <see cref="SetThrottle"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="SetThrottle">SetThrottles</see> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(SetThrottle left, SetThrottle right)
        {
            return !Equals(left, right);
        }
    }

    /// <summary>
    /// ACKs a throttle command
    /// </summary>
    public sealed class SetThrottleAck
    {
        private SetThrottleAck() { }
        // ReSharper disable once InconsistentNaming
        private static readonly SetThrottleAck _instance = new SetThrottleAck();

        /// <summary>
        /// TBD
        /// </summary>
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

        internal readonly IActorRef ThrottlerActor;

        internal AtomicReference<ThrottleMode> OutboundThrottleMode = new AtomicReference<ThrottleMode>(Unthrottled.Instance);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="wrappedHandle">TBD</param>
        /// <param name="throttlerActor">TBD</param>
        public ThrottlerHandle(AssociationHandle wrappedHandle, IActorRef throttlerActor) : base(wrappedHandle, ThrottleTransportAdapter.Scheme)
        {
            ThrottlerActor = throttlerActor;
        }

        /// <inheritdoc/>
        public override bool Write(ByteString payload)
        {
            var tokens = payload.Length;
            //need to declare recursive delegates first before they can self-reference
            //might want to consider making this consumer function strongly typed: http://blogs.msdn.com/b/wesdyer/archive/2007/02/02/anonymous-recursion-in-c.aspx
            bool TryConsume(ThrottleMode currentBucket)
            {
                var timeOfSend = MonotonicClock.GetNanos();
                var res = currentBucket.TryConsumeTokens(timeOfSend, tokens);
                var newBucket = res.Item1;
                var allow = res.Item2;
                if (allow)
                {
                    return OutboundThrottleMode.CompareAndSet(currentBucket, newBucket) || TryConsume(OutboundThrottleMode.Value);
                }
                return false;
            }

            var throttleMode = OutboundThrottleMode.Value;
            if (throttleMode is Blackhole) return true;

            var success = TryConsume(OutboundThrottleMode.Value);
            return success && WrappedHandle.Write(payload);
        }

        /// <inheritdoc/>
        public override void Disassociate()
        {
            ThrottlerActor.Tell(PoisonPill.Instance);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        public void DisassociateWithFailure(DisassociateInfo reason)
        {
            ThrottlerActor.Tell(new ThrottledAssociation.FailWith(reason));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class ThrottledAssociation : FSM<ThrottledAssociation.ThrottlerState, ThrottledAssociation.IThrottlerData>, ILoggingFSM
    {
        #region ThrottledAssociation FSM state and data classes

        private const string DequeueTimerName = "dequeue";

        /// <summary>
        /// TBD
        /// </summary>
        sealed class Dequeue { }

        /// <summary>
        /// TBD
        /// </summary>
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

        /// <summary>
        /// TBD
        /// </summary>
        internal interface IThrottlerData { }

        /// <summary>
        /// TBD
        /// </summary>
        internal class Uninitialized : IThrottlerData
        {
            private Uninitialized() { }
            // ReSharper disable once InconsistentNaming
            private static readonly Uninitialized _instance = new Uninitialized();
            /// <summary>
            /// TBD
            /// </summary>
            public static Uninitialized Instance { get { return _instance; } }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class ExposedHandle : IThrottlerData
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="handle">TBD</param>
            public ExposedHandle(ThrottlerHandle handle)
            {
                Handle = handle;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public ThrottlerHandle Handle { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class FailWith
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="failReason">TBD</param>
            public FailWith(DisassociateInfo failReason)
            {
                FailReason = failReason;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public DisassociateInfo FailReason { get; private set; }
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        protected IActorRef Manager;
        /// <summary>
        /// TBD
        /// </summary>
        protected IAssociationEventListener AssociationHandler;
        /// <summary>
        /// TBD
        /// </summary>
        protected AssociationHandle OriginalHandle;
        /// <summary>
        /// TBD
        /// </summary>
        protected bool Inbound;

        /// <summary>
        /// TBD
        /// </summary>
        protected ThrottleMode InboundThrottleMode;
        /// <summary>
        /// TBD
        /// </summary>
        protected Queue<ByteString> ThrottledMessages = new Queue<ByteString>();
        /// <summary>
        /// TBD
        /// </summary>
        protected IHandleEventListener UpstreamListener;

        /// <summary>
        /// Used for decoding certain types of throttled messages on-the-fly
        /// </summary>
        private readonly AkkaPduProtobuffCodec _codec;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="manager">TBD</param>
        /// <param name="associationHandler">TBD</param>
        /// <param name="originalHandle">TBD</param>
        /// <param name="inbound">TBD</param>
        public ThrottledAssociation(IActorRef manager, IAssociationEventListener associationHandler, AssociationHandle originalHandle, bool inbound)
        {
            _codec = new AkkaPduProtobuffCodec(Context.System);
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
                    var mode = @event.FsmEvent.AsInstanceOf<ThrottleMode>();
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
                            var self = Self;
                            exposedHandle.ReadHandlerSource.Task.ContinueWith(
                                r => new ThrottlerManager.Listener(r.Result),
                                TaskContinuationOptions.ExecuteSynchronously)
                                .PipeTo(self);
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
                    if (mode is Blackhole) ThrottledMessages = new Queue<ByteString>();
                    CancelTimer(DequeueTimerName);
                    if (ThrottledMessages.Any())
                        ScheduleDequeue(InboundThrottleMode.TimeToAvailable(MonotonicClock.GetNanos(), ThrottledMessages.Peek().Length));
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
                        InboundThrottleMode = InboundThrottleMode.TryConsumeTokens(MonotonicClock.GetNanos(),
                            payload.Length).Item1;
                        if (ThrottledMessages.Any())
                            ScheduleDequeue(InboundThrottleMode.TimeToAvailable(MonotonicClock.GetNanos(), ThrottledMessages.Peek().Length));
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
                    if (UpstreamListener != null) UpstreamListener.Notify(new Disassociated(reason));
                    return Stop();
                }

                return null;
            });

            if (Inbound)
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
                var pdu = _codec.DecodePdu(b);
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
                SetTimer(DequeueTimerName, new Dequeue(), delay, repeat: false);
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
                    var res = InboundThrottleMode.TryConsumeTokens(MonotonicClock.GetNanos(), tokens);
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
                        ScheduleDequeue(InboundThrottleMode.TimeToAvailable(MonotonicClock.GetNanos(), tokens));
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

