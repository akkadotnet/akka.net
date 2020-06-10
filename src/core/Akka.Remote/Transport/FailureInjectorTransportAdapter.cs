//-----------------------------------------------------------------------
// <copyright file="FailureInjectorTransportAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal;
using Google.Protobuf;
using System.Runtime.Serialization;

namespace Akka.Remote.Transport
{
    /// <summary>
    /// Provider implementation for creating <see cref="FailureInjectorTransportAdapter"/> instances.
    /// </summary>
    public class FailureInjectorProvider : ITransportAdapterProvider
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="wrappedTransport">TBD</param>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public Transport Create(Transport wrappedTransport, ExtendedActorSystem system)
        {
            return new FailureInjectorTransportAdapter(wrappedTransport, system);
        }
    }

    /// <summary>
    /// This exception is used to indicate a simulated failure in an association.
    /// </summary>
    public sealed class FailureInjectorException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FailureInjectorException"/> class.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        public FailureInjectorException(string msg)
        {
            Msg = msg;
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="FailureInjectorException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        private FailureInjectorException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif

        /// <summary>
        /// Retrieves the message of the simulated failure.
        /// </summary>
        public string Msg { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class FailureInjectorTransportAdapter : AbstractTransportAdapter, IAssociationEventListener
    {
#region Internal message classes

        /// <summary>
        /// TBD
        /// </summary>
        public const string FailureInjectorSchemeIdentifier = "gremlin";

        /// <summary>
        /// TBD
        /// </summary>
        public interface IFailureInjectorCommand { }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class All
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="mode">TBD</param>
            public All(IGremlinMode mode)
            {
                Mode = mode;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IGremlinMode Mode { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class One
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="remoteAddress">TBD</param>
            /// <param name="mode">TBD</param>
            public One(Address remoteAddress, IGremlinMode mode)
            {
                Mode = mode;
                RemoteAddress = remoteAddress;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Address RemoteAddress { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            public IGremlinMode Mode { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public interface IGremlinMode { }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class PassThru : IGremlinMode
        {
            private PassThru() { }
// ReSharper disable once InconsistentNaming
            private static readonly PassThru _instance = new PassThru();

            /// <summary>
            /// TBD
            /// </summary>
            public static PassThru Instance
            {
                get { return _instance; }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Drop : IGremlinMode
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="outboundDropP">TBD</param>
            /// <param name="inboundDropP">TBD</param>
            public Drop(double outboundDropP, double inboundDropP)
            {
                InboundDropP = inboundDropP;
                OutboundDropP = outboundDropP;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public double OutboundDropP { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            public double InboundDropP { get; private set; }
        }

#endregion

        /// <summary>
        /// TBD
        /// </summary>
        public readonly ExtendedActorSystem ExtendedActorSystem;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="wrappedTransport">TBD</param>
        /// <param name="extendedActorSystem">TBD</param>
        public FailureInjectorTransportAdapter(Transport wrappedTransport, ExtendedActorSystem extendedActorSystem) : base(wrappedTransport)
        {
            ExtendedActorSystem = extendedActorSystem;
            _log = Logging.GetLogger(ExtendedActorSystem, this);
            _shouldDebugLog = ExtendedActorSystem.Settings.Config.GetBoolean("akka.remote.gremlin.debug", false);
        }

        private ILoggingAdapter _log;
        private Random Rng
        {
            get { return ThreadLocalRandom.Current; }
        }

        private bool _shouldDebugLog;
        private volatile IAssociationEventListener _upstreamListener = null;
        private readonly ConcurrentDictionary<Address,IGremlinMode> addressChaosTable = new ConcurrentDictionary<Address, IGremlinMode>();
        private volatile IGremlinMode _allMode = PassThru.Instance;

        /// <summary>
        /// TBD
        /// </summary>
        protected int MaximumOverhead = 0;

#region AbstractTransportAdapter members

        // ReSharper disable once InconsistentNaming
        private static readonly SchemeAugmenter _augmenter = new SchemeAugmenter(FailureInjectorSchemeIdentifier);
        /// <summary>
        /// TBD
        /// </summary>
        protected override SchemeAugmenter SchemeAugmenter
        {
            get { return _augmenter; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        public override Task<bool> ManagementCommand(object message)
        {
            if (message is All)
            {
                var all = message as All;
                _allMode = all.Mode;
                return Task.FromResult(true);
            }
            
            if (message is One)
            {
                var one = message as One;
                //  don't care about the protocol part - we are injected in the stack anyway!
                addressChaosTable.AddOrUpdate(NakedAddress(one.RemoteAddress), address => one.Mode, (address, mode) => one.Mode);
                return Task.FromResult(true);
            }

            return WrappedTransport.ManagementCommand(message);
        }

#endregion

#region IAssociationEventListener members

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="listenAddress">TBD</param>
        /// <param name="listenerTask">TBD</param>
        /// <returns>TBD</returns>
        protected override Task<IAssociationEventListener> InterceptListen(Address listenAddress, Task<IAssociationEventListener> listenerTask)
        {
            _log.Warning("FailureInjectorTransport is active on this system. Gremlins might munch your packets.");
            listenerTask.ContinueWith(tr =>
            {
                // Side effecting: As this class is not an actor, the only way to safely modify state is through volatile vars.
                // Listen is called only during the initialization of the stack, and upstreamListener is not read before this
                // finishes.
                _upstreamListener = tr.Result;
            }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);
            return Task.FromResult((IAssociationEventListener)this);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="statusPromise">TBD</param>
        /// <exception cref="FailureInjectorException">TBD</exception>
        protected override void InterceptAssociate(Address remoteAddress, TaskCompletionSource<AssociationHandle> statusPromise)
        {
            // Association is simulated to be failed if there was either an inbound or outbound message drop
            if (ShouldDropInbound(remoteAddress, new object(), "interceptAssociate") ||
                ShouldDropOutbound(remoteAddress, new object(), "interceptAssociate"))
            {
                statusPromise.SetException(
                    new FailureInjectorException("Simulated failure of association to " + remoteAddress));
            }
            else
            {
               WrappedTransport.Associate(remoteAddress).ContinueWith(tr =>
               {
                   var handle = tr.Result;
                   addressChaosTable.AddOrUpdate(NakedAddress(handle.RemoteAddress), address => PassThru.Instance,
                       (address, mode) => PassThru.Instance);
                   statusPromise.SetResult(new FailureInjectorHandle(handle, this));
               }, TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ev">TBD</param>
        public void Notify(IAssociationEvent ev)
        {
            if (ev is InboundAssociation && ShouldDropInbound(ev.AsInstanceOf<InboundAssociation>().Association.RemoteAddress, ev, "notify"))
            {
                //ignore
            }
            else
            {
                if (_upstreamListener == null)
                {
                }
                else
                {
                    _upstreamListener.Notify(InterceptInboundAssociation(ev));
                }
            }
        }

#endregion

#region Internal methods

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="instance">TBD</param>
        /// <param name="debugMessage">TBD</param>
        /// <returns>TBD</returns>
        public bool ShouldDropInbound(Address remoteAddress, object instance, string debugMessage)
        {
            var mode = ChaosMode(remoteAddress);
            if (mode is PassThru) return false;
            if (mode is Drop)
            {
                var drop = mode as Drop;
                if (Rng.NextDouble() <= drop.InboundDropP)
                {
                    if (_shouldDebugLog) _log.Debug("Dropping inbound [{0}] for [{1}] {2}", instance.GetType(),
                         remoteAddress, debugMessage);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="instance">TBD</param>
        /// <param name="debugMessage">TBD</param>
        /// <returns>TBD</returns>
        public bool ShouldDropOutbound(Address remoteAddress, object instance, string debugMessage)
        {
            var mode = ChaosMode(remoteAddress);
            if (mode is PassThru) return false;
            if (mode is Drop)
            {
                var drop = mode as Drop;
                if (Rng.NextDouble() <= drop.OutboundDropP)
                {
                    if (_shouldDebugLog) 
                        _log.Debug("Dropping outbound [{0}] for [{1}] {2}", instance.GetType(), remoteAddress, debugMessage);
                    return true;
                }
            }

            return false;
        }

        private IAssociationEvent InterceptInboundAssociation(IAssociationEvent ev)
        {
            if (ev is InboundAssociation) return new InboundAssociation(new FailureInjectorHandle(ev.AsInstanceOf<InboundAssociation>().Association, this));
            return ev;
        }

        private static Address NakedAddress(Address address)
        {
            return address.WithProtocol(string.Empty)
                .WithSystem(string.Empty);
        }

        private IGremlinMode ChaosMode(Address remoteAddress)
        {
            if (addressChaosTable.TryGetValue(NakedAddress(remoteAddress), out var mode))
                return mode;

            return PassThru.Instance;
        }

#endregion
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class FailureInjectorHandle : AbstractTransportAdapterHandle, IHandleEventListener
    {
        private readonly FailureInjectorTransportAdapter _gremlinAdapter;
        private volatile IHandleEventListener _upstreamListener = null;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="wrappedHandle">TBD</param>
        /// <param name="gremlinAdapter">TBD</param>
        public FailureInjectorHandle(AssociationHandle wrappedHandle, FailureInjectorTransportAdapter gremlinAdapter)
            : base(wrappedHandle, FailureInjectorTransportAdapter.FailureInjectorSchemeIdentifier)
        {
            _gremlinAdapter = gremlinAdapter;
            ReadHandlerSource.Task.ContinueWith(tr =>
            {
                _upstreamListener = tr.Result;
                WrappedHandle.ReadHandlerSource.SetResult(this);
            }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="payload">TBD</param>
        /// <returns>TBD</returns>
        public override bool Write(ByteString payload)
        {
            if (!_gremlinAdapter.ShouldDropOutbound(WrappedHandle.RemoteAddress, payload, "handler.write"))
                return WrappedHandle.Write(payload);
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void Disassociate()
        {
            WrappedHandle.Disassociate();
        }

#region IHandleEventListener members

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ev">TBD</param>
        public void Notify(IHandleEvent ev)
        {
            if (!_gremlinAdapter.ShouldDropInbound(WrappedHandle.RemoteAddress, ev, "handler.notify"))
            {
                _upstreamListener.Notify(ev);
            }
        }

#endregion
    }
}

