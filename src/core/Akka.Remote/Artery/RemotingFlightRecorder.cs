using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Akka.Actor;

namespace Akka.Remote.Artery
{
    internal interface IRemotingFlightRecorder : IExtension
    {
        void TransportMediaDriverStarted(string directoryName);
        void TransportStarted();
        void TransportAeronErrorLogStarted();
        void TransportTaskRunnerStarted();
        void TransportUniqueAddressSet(UniqueAddress uniqueAddress);
        void TransportMaterializerStarted();
        void TransportStartupFinished();
        void TransportKillSwitchPulled();
        void TransportStopped();
        void TransportAeronErrorLogTaskStopped();
        void TransportMediaFileDeleted();
        void TransportSendQueueOverflow(int queueIndex);
        void TransportStopIdleOutbound(Address remoteAddress, int queueIndex);
        void TransportQuarantined(Address remoteAddress, long uid);
        void TransportRemoveQuarantined(Address remoteAddress);
        void TransportRestartOutbound(Address remoteAddress, string streamName);
        void TransportRestartInbound(UniqueAddress remoteAddress, string streamName);

        void AeronSinkStarted(string channel, int streamId);
        void AeronSinkTaskRunnerRemoved(string channel, int streamId);
        void AeronSinkPublicationClosed(string channel, int streamId);
        void AeronSinkPublicationClosedUnexpectedly(string channel, int streamId);
        void AeronSinkStopped(string channel, int streamId);
        void AeronSinkEnvelopeGrabbed(string channel, int streamId);
        void AeronSinkEnvelopeOffered(string channel, int streamId);
        void AeronSinkGaveUpEnvelope(string cause);
        void AeronSinkDelegateToTaskRunner(long countBeforeDelegate);
        void AronSinkReturnFromTaskRunner(long nanosSinceTaskStartTime);

        void AeronSourceStarted(string channel, int streamId);
        void AeronSourceStopped(string channel, int streamId);
        void AeronSourceReceived(int size);
        void AeronSourceDelegateToTaskRunner(long countBeforeDelegate);
        void AeronSourceReturnFromTaskRunner(long nanosSinceTaskStartTime);

        void CompressionActorRefAdvertisement(long uid);
        void CompressionClassManifestAdvertisement(long uid);

        void TcpOutboundConnected(Address remoteAddress, string streamName);
        void TcpOutboundSent(int size);

        void TcpInboundBound(string bindHost, InetSocketAddress address);
        void TcpInboundUnbound(UniqueAddress localAddress);
        void TcpInboundConnected(InetSocketAddress remoteAddress);
        void TcpInboundReceived(int size);
    }

    internal class RemotingFlightRecorderExtension : ExtensionIdProvider<IRemotingFlightRecorder>
    {
        public static IRemotingFlightRecorder Get(ActorSystem system)
        {
            return system.WithExtension<IRemotingFlightRecorder, RemotingFlightRecorderExtension>();
        }

        public override IRemotingFlightRecorder CreateExtension(ExtendedActorSystem system)
        {
            if (system.Settings.Config.GetBoolean("akka.clr-flight-recorder.enabled"))
            {
                // ARTERY: supposed to return an instance of akka.remote.artery.jfr.JFRRemotingFlightRecorder
            }
            return new NoOpRemotingFlightRecorder();
        }
    }

    internal class NoOpRemotingFlightRecorder : IRemotingFlightRecorder
    {
        public void TransportMediaDriverStarted(string directoryName) {}
        public void TransportStarted() {}
        public void TransportAeronErrorLogStarted() {}
        public void TransportTaskRunnerStarted() {}
        public void TransportUniqueAddressSet() {}
        public void TransportMaterializerStarted() {}
        public void TransportStartupFinished() {}
        public void TransportKillSwitchPulled() {}
        public void TransportStopped() {}
        public void TransportAeronErrorLogTaskStopped() {}
        public void TransportMediaFileDeleted() {}
        public void TransportSendQueueOverflow(int queueIndex) {}
        public void TransportStopIdleOutbound(Address remoteAddress, int queueIndex) {}
        public void TransportQuarantined(Address remoteAddress, long uid) {}
        public void TransportRemoveQuarantined(Address remoteAddress) {}
        public void TransportRestartOutbound(Address remoteAddress, string streamName) {}
        public void TransportRestartInbound(UniqueAddress remoteAddress, string streamName) {}

        public void AeronSinkStarted(string channel, int streamId) {}
        public void AeronSinkTaskRunnerRemoved(string channel, int streamId) {}
        public void AeronSinkPublicationClosed(string channel, int streamId) {}
        public void AeronSinkPublicationClosedUnexpectedly(string channel, int streamId) {}
        public void AeronSinkStopped(string channel, int streamId) {}
        public void AeronSinkEnvelopeGrabbed(string channel, int streamId) {}
        public void AeronSinkEnvelopeOffered(string channel, int streamId) {}
        public void AeronSinkGaveUpEnvelope(string cause) {}
        public void AeronSinkDelegateToTaskRunner(long countBeforeDelegate) {}
        public void AronSinkReturnFromTaskRunner(long nanosSinceTaskStartTime) {}

        public void AeronSourceStarted(string channel, int streamId) {}
        public void AeronSourceStopped(string channel, int streamId) {}
        public void AeronSourceReceived(int size) {}
        public void AeronSourceDelegateToTaskRunner(long countBeforeDelegate) {}
        public void AeronSourceReturnFromTaskRunner(long nanosSinceTaskStartTime) {}

        public void CompressionActorRefAdvertisement(long uid) { }
        public void CompressionClassManifestAdvertisement(long uid) {}

        public void TcpOutboundConnected(Address remoteAddress, string streamName) {}
        public void TcpOutboundSent(int size) {}
        public void TcpInboundBound(string bindHost, InetSocketAddress address) {}
        public void TcpInboundUnbound(UniqueAddress localAddress) {}
        public void TcpInboundConnected(InetSocketAddress remoteAddress) {}
        public void TcpInboundReceived(int size) {}
    }
}
