using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics;
using System.Threading;
using Akka.Actor;
using Akka.Remote.Transport;
using Akka.Streams;
using Akka.Util;
using Akka.Event;
using Akka.Remote.Artery.Settings;

namespace Akka.Remote.Artery
{
    // ARTERY: Incomplete implementation
    internal class Association : IOutboundContext
    {
        /// <summary>
        /// Holds reference to shared state of Association - *access only via helper methods*
        /// </summary>
        private volatile AssociationState _sharedStateDoNotCallMeDirectly = new AssociationState();

        public AssociationState AssociationState => Volatile.Read(ref _sharedStateDoNotCallMeDirectly);

        private readonly ArteryTransport _transport;
        private readonly ActorMaterializer _materializer;
        private readonly ActorMaterializer _controlMaterializer;
        private readonly WildcardIndex<NotUsed> _largeMessageDestinations;
        private readonly WildcardIndex<NotUsed> _priorityMessageDestinations;
        // ARTERY: ObjectPool isn't implemented
        // private readonly ObjectPool<ReusableOutboundEnvelope> _outboundEnvelopePool;

        private readonly ILoggingAdapter _log;
        // ARTERY: RemotingFlightRecorder isn't implemented
        // private readonly RemotingFlightRecorder _flightRecorder;
        private readonly AdvancedSettings _advancedSettings;

        public Address RemoteAddress { get; }
        public IControlMessageSubject ControlSubject { get; }
        public UniqueAddress LocalAddress { get; }
        public ArterySettings Settings { get; }

        public Association(
            ArteryTransport transport,
            ActorMaterializer materializer,
            ActorMaterializer controlMaterializer,
            Address remoteAddress,
            IControlMessageSubject controlSubject,
            WildcardIndex<NotUsed> largeMessageDestinations,
            WildcardIndex<NotUsed> priorityMessageDestinations
            // ARTERY: ObjectPool isn't implemented
            // ObjectPool<ReusableOutboundEnvelope> outboundEnvelopePool
            )
        {
            _transport = transport;
            _materializer = materializer;
            _controlMaterializer = controlMaterializer;
            RemoteAddress = remoteAddress;
            ControlSubject = controlSubject;
            _largeMessageDestinations = largeMessageDestinations;
            _priorityMessageDestinations = priorityMessageDestinations;
            // ARTERY: ObjectPool isn't implemented
            // _outboundEnvelopePool = outboundEnvelopePool;

            System.Diagnostics.Debug.Assert(RemoteAddress.Port.HasValue);

            _log = Logging.GetLogger(_transport.System, GetType());
            // ARTERY: RemotingFlightRecorder isn't implemented
            // _flightRecorder = _transport.FlightRecorder;

            Settings = _transport.Settings;
            _advancedSettings = Settings.Advanced;
            // ====================== last line
        }

        public bool SwapState(AssociationState oldState, AssociationState newState)
        {
            // ARTERY: stub function
            return false;
        }

        public int SendTerminationHint(IActorRef replyTo)
        {
            // ARTERY: stub function
            return 0;
        }

        public void Quarantine(string reason)
        {
            throw new NotImplementedException();
        }

        public void SendControl(IControlMessage message)
        {
            throw new NotImplementedException();
        }

        public bool IsOrdinaryMessageStreamActive()
        {
            throw new NotImplementedException();
        }
    }

    // ARTERY: Incomplete implementation
    internal class AssociationRegistry
    {

    }
}
