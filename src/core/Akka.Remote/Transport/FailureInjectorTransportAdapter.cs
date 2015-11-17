//-----------------------------------------------------------------------
// <copyright file="FailureInjectorTransportAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal;
using Google.ProtocolBuffers;
using System.Runtime.Serialization;

namespace Akka.Remote.Transport
{
    /// <summary>
    /// Provider implementation for creating <see cref="FailureInjectorTransportAdapter"/> instances.
    /// </summary>
    public class FailureInjectorProvider : ITransportAdapterProvider
    {
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

        /// <summary>
        /// Initializes a new instance of the <see cref="FailureInjectorException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        private FailureInjectorException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

        /// <summary>
        /// Retrieves the message of the simulated failure.
        /// </summary>
        public string Msg { get; private set; }
    }

    internal class FailureInjectorTransportAdapter : AbstractTransportAdapter, IAssociationEventListener
    {
        #region Internal message classes

        public const string FailureInjectorSchemeIdentifier = "gremlin";

        public interface IFailureInjectorCommand { }

        public sealed class All
        {
            public All(IGremlinMode mode)
            {
                Mode = mode;
            }

            public IGremlinMode Mode { get; private set; }
        }

        public sealed class One
        {
            public One(Address remoteAddress, IGremlinMode mode)
            {
                Mode = mode;
                RemoteAddress = remoteAddress;
            }

            public Address RemoteAddress { get; private set; }

            public IGremlinMode Mode { get; private set; }
        }

        public interface IGremlinMode { }

        public sealed class PassThru : IGremlinMode
        {
            private PassThru() { }
// ReSharper disable once InconsistentNaming
            private static readonly PassThru _instance = new PassThru();

            public static PassThru Instance
            {
                get { return _instance; }
            }
        }

        public sealed class Drop : IGremlinMode
        {
            public Drop(double outboundDropP, double inboundDropP)
            {
                InboundDropP = inboundDropP;
                OutboundDropP = outboundDropP;
            }

            public double OutboundDropP { get; private set; }

            public double InboundDropP { get; private set; }
        }

        #endregion

        public readonly ExtendedActorSystem ExtendedActorSystem;

        public FailureInjectorTransportAdapter(Transport wrappedTransport, ExtendedActorSystem extendedActorSystem) : base(wrappedTransport)
        {
            ExtendedActorSystem = extendedActorSystem;
            _log = Logging.GetLogger(ExtendedActorSystem, this);
            _shouldDebugLog = ExtendedActorSystem.Settings.Config.GetBoolean("akka.remote.gremlin.debug");
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

        protected int MaximumOverhead = 0;

        #region AbstractTransportAdapter members

        // ReSharper disable once InconsistentNaming
        private static readonly SchemeAugmenter _augmenter = new SchemeAugmenter(FailureInjectorSchemeIdentifier);
        protected override SchemeAugmenter SchemeAugmenter
        {
            get { return _augmenter; }
        }

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
            IGremlinMode mode;
            if (addressChaosTable.TryGetValue(NakedAddress(remoteAddress), out mode))
            {
                return mode;
            }

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

        public override bool Write(ByteString payload)
        {
            if (!_gremlinAdapter.ShouldDropOutbound(WrappedHandle.RemoteAddress, payload, "handler.write"))
                return WrappedHandle.Write(payload);
            return true;
        }

        public override void Disassociate()
        {
            WrappedHandle.Disassociate();
        }

        #region IHandleEventListener members

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

