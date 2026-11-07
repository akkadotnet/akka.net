//-----------------------------------------------------------------------
// <copyright file="FailureInjectorTransportAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// Creates a failure-injecting wrapper around the supplied transport.
        /// </summary>
        /// <param name="wrappedTransport">Transport whose associations and messages will be subject to configured failure injection.</param>
        /// <param name="system">Actor system used to configure and initialize the adapter.</param>
        /// <returns>The failure-injecting transport adapter.</returns>
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

    /// <summary>
    /// Transport adapter that can probabilistically drop inbound or outbound association events and payloads.
    /// </summary>
    public class FailureInjectorTransportAdapter : AbstractTransportAdapter, IAssociationEventListener
    {
#region Internal message classes

        /// <summary>
        /// Scheme identifier added to addresses exposed by this adapter.
        /// </summary>
        public const string FailureInjectorSchemeIdentifier = "gremlin";

        /// <summary>
        /// Management command that applies a gremlin mode to all remote addresses.
        /// </summary>
        public interface IFailureInjectorCommand { }

        /// <summary>
        /// Applies the specified mode to all connections managed by this adapter.
        /// </summary>
        public sealed class All
        {
            /// <summary>
            /// Creates a command to apply a gremlin mode to all addresses.
            /// </summary>
            /// <param name="mode">Mode to use for all addresses.</param>
            public All(IGremlinMode mode)
            {
                Mode = mode;
            }

            /// <summary>
            /// Gremlin mode applied to all addresses.
            /// </summary>
            public IGremlinMode Mode { get; private set; }
        }

        /// <summary>
        /// Management command that applies a gremlin mode to one remote address.
        /// </summary>
        public sealed class One
        {
            /// <summary>
            /// Creates a command to apply a gremlin mode to one remote address.
            /// </summary>
            /// <param name="remoteAddress">Remote address whose connections will use the mode.</param>
            /// <param name="mode">Gremlin mode to apply to that address.</param>
            public One(Address remoteAddress, IGremlinMode mode)
            {
                Mode = mode;
                RemoteAddress = remoteAddress;
            }

            /// <summary>
            /// Remote address whose connections use the mode.
            /// </summary>
            public Address RemoteAddress { get; private set; }

            /// <summary>
            /// Gremlin mode applied to the remote address.
            /// </summary>
            public IGremlinMode Mode { get; private set; }
        }

        /// <summary>
        /// Describes a failure-injection behavior supported by this adapter.
        /// </summary>
        public interface IGremlinMode { }

        /// <summary>
        /// Mode that passes association events and payloads through without dropping them.
        /// </summary>
        public sealed class PassThru : IGremlinMode
        {
            private PassThru() { }
            public static PassThru Instance { get; } = new();
        }

        /// <summary>
        /// Mode that independently drops inbound and outbound traffic according to configured probabilities.
        /// </summary>
        public sealed class Drop : IGremlinMode
        {
            /// <summary>
            /// Creates a probabilistic drop mode for inbound and outbound traffic.
            /// </summary>
            /// <param name="outboundDropP">Probability used when deciding whether to drop each outbound item.</param>
            /// <param name="inboundDropP">Probability used when deciding whether to drop each inbound item.</param>
            public Drop(double outboundDropP, double inboundDropP)
            {
                InboundDropP = inboundDropP;
                OutboundDropP = outboundDropP;
            }

            /// <summary>
            /// Probability used when deciding whether to drop each outbound item.
            /// </summary>
            public double OutboundDropP { get; private set; }

            /// <summary>
            /// Probability used when deciding whether to drop each inbound item.
            /// </summary>
            public double InboundDropP { get; private set; }
        }

#endregion

        /// <summary>
        /// Actor system used to configure this adapter and write its logs.
        /// </summary>
        public readonly ExtendedActorSystem ExtendedActorSystem;

        /// <summary>
        /// Creates a failure-injecting adapter for an underlying transport.
        /// </summary>
        /// <param name="wrappedTransport">Underlying transport to wrap.</param>
        /// <param name="extendedActorSystem">Actor system whose configuration controls the adapter.</param>
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
        private readonly ConcurrentDictionary<Address,IGremlinMode> addressChaosTable = new();
        private volatile IGremlinMode _allMode = PassThru.Instance;

        /// <summary>
        /// Additional payload overhead accounted for by an adapter; initialized to zero.
        /// </summary>
        protected int MaximumOverhead = 0;

#region AbstractTransportAdapter members

        // ReSharper disable once InconsistentNaming
        private static readonly SchemeAugmenter _augmenter = new(FailureInjectorSchemeIdentifier);
        /// <summary>
        /// Adds the failure-injector scheme identifier to addresses exposed by the wrapped transport.
        /// </summary>
        protected override SchemeAugmenter SchemeAugmenter
        {
            get { return _augmenter; }
        }

        /// <summary>
        /// Applies a global or per-address gremlin mode, or forwards an unrecognized command.
        /// </summary>
        /// <param name="message">Global/per-address gremlin command or another transport management command.</param>
        /// <returns>A task that completes with <see langword="true"/> when this adapter accepts the command, or the wrapped transport's result otherwise.</returns>
        public override Task<bool> ManagementCommand(object message)
        {
            if (message is All all)
            {
                _allMode = all.Mode;
                return Task.FromResult(true);
            }
            
            if (message is One one)
            {
                //  don't care about the protocol part - we are injected in the stack anyway!
                addressChaosTable.AddOrUpdate(NakedAddress(one.RemoteAddress), _ => one.Mode, (_, _) => one.Mode);
                return Task.FromResult(true);
            }

            return WrappedTransport.ManagementCommand(message);
        }

#endregion

#region IAssociationEventListener members

        /// <summary>
        /// Installs this adapter as the listener for inbound associations and retains the upstream listener.
        /// </summary>
        /// <param name="listenAddress">Address on which the wrapped transport is listening.</param>
        /// <param name="listenerTask">Task that completes with the upstream association event listener.</param>
        /// <returns>A task that completes with this adapter as the wrapped transport's association listener.</returns>
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
        /// Applies inbound and outbound drop rules before requesting an association from the wrapped transport.
        /// </summary>
        /// <param name="remoteAddress">Remote address to associate with.</param>
        /// <param name="statusPromise">Completion source for the resulting association handle.</param>
        /// <remarks>When a configured drop rule simulates an association failure, the association task is faulted with <see cref="FailureInjectorException"/>.</remarks>
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
                   addressChaosTable.AddOrUpdate(NakedAddress(handle.RemoteAddress), _ => PassThru.Instance,
                       (_, _) => PassThru.Instance);
                   statusPromise.SetResult(new FailureInjectorHandle(handle, this));
               }, TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        /// <summary>
        /// Drops configured inbound association attempts and forwards all other events to the upstream listener.
        /// </summary>
        /// <param name="ev">Association event received from the wrapped transport.</param>
        public void Notify(IAssociationEvent ev)
        {
            if (ev is InboundAssociation inboundAssociation && ShouldDropInbound(inboundAssociation.Association.RemoteAddress, ev, "notify"))
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
        /// Returns whether the configured mode drops this inbound item for the remote address.
        /// </summary>
        /// <param name="remoteAddress">Remote address associated with the inbound item.</param>
        /// <param name="instance">Item considered for dropping, used to identify it in debug logs.</param>
        /// <param name="debugMessage">Context appended to the optional debug log entry.</param>
        /// <returns><see langword="true"/> if the active drop mode discards the item; otherwise <see langword="false"/>.</returns>
        public bool ShouldDropInbound(Address remoteAddress, object instance, string debugMessage)
        {
            var mode = ChaosMode(remoteAddress);
            if (mode is PassThru) return false;
            if (mode is Drop drop)
            {
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
        /// Returns whether the configured mode drops this outbound item for the remote address.
        /// </summary>
        /// <param name="remoteAddress">Remote address associated with the outbound item.</param>
        /// <param name="instance">Item considered for dropping, used to identify it in debug logs.</param>
        /// <param name="debugMessage">Context appended to the optional debug log entry.</param>
        /// <returns><see langword="true"/> if the active drop mode discards the item; otherwise <see langword="false"/>.</returns>
        public bool ShouldDropOutbound(Address remoteAddress, object instance, string debugMessage)
        {
            var mode = ChaosMode(remoteAddress);
            if (mode is PassThru) return false;
            if (mode is Drop drop)
            {
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
            if (ev is InboundAssociation inboundAssociation)
            {
                return new InboundAssociation(new FailureInjectorHandle(inboundAssociation.Association, this));
            }
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
        /// Wraps an underlying association handle with inbound and outbound failure injection.
        /// </summary>
        /// <param name="wrappedHandle">Underlying association handle to wrap.</param>
        /// <param name="gremlinAdapter">Adapter that decides whether inbound and outbound events are dropped.</param>
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
        /// Drops the outbound payload according to the configured mode, or writes it to the underlying handle.
        /// </summary>
        /// <param name="payload">Payload bytes to write.</param>
        /// <returns><see langword="false"/> if the underlying handle rejected the write; otherwise <see langword="true"/>, including when the payload was intentionally dropped.</returns>
        public override bool Write(ByteString payload)
        {
            if (!_gremlinAdapter.ShouldDropOutbound(WrappedHandle.RemoteAddress, payload, "handler.write"))
                return WrappedHandle.Write(payload);
            return true;
        }

        /// <summary>
        /// Disassociates the underlying transport handle.
        /// </summary>

#pragma warning disable CS0672
        public override void Disassociate()
#pragma warning restore CS0672
        {
#pragma warning disable CS0618
            WrappedHandle.Disassociate();
#pragma warning restore CS0618
        }

        #region IHandleEventListener members

        /// <summary>
        /// Drops configured inbound events before notifying the registered upstream listener.
        /// </summary>
        /// <param name="ev">Inbound event received from the underlying association.</param>
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

