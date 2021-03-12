using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery.Compress;
using Akka.Remote.Serialization;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class Encoder : GraphStageWithMaterializedValue<FlowShape<IOutboundEnvelope, EnvelopeBuffer>, Encoder.IOutboundCompressionAccess>
    {
        public interface IOutboundCompressionAccess
        {
            Task<Done> ChangeActorRefCompression(CompressionTable<IActorRef> table);
            Task<Done> ChangeClassManifestCompression(CompressionTable<string> table);
            Task<Done> ClearCompression();
        }

        private readonly UniqueAddress _uniqueLocalAddress;
        private readonly ExtendedActorSystem _system;
        private readonly ObjectPool<ReusableOutboundEnvelope> _outboundEnvelopePool;
        private readonly EnvelopeBufferPool _bufferPool;
        private readonly bool _debugLogSend;
        private readonly byte _version;

        private readonly Inlet<IOutboundEnvelope> _in = new Inlet<IOutboundEnvelope>("Artery.Encoder.in");
        private readonly Outlet<EnvelopeBuffer> _out = new Outlet<EnvelopeBuffer>("Artery.Encoder.out");
        private readonly FlowShape<IOutboundEnvelope, EnvelopeBuffer> _shape;

        public Encoder(
            UniqueAddress uniqueLocalAddress,
            ExtendedActorSystem system,
            ObjectPool<ReusableOutboundEnvelope> outboundEnvelopePool,
            EnvelopeBufferPool bufferPool,
            int streamId, // not used
            bool debugLogSend,
            byte version)
        {
            _uniqueLocalAddress = uniqueLocalAddress;
            _system = system;
            _bufferPool = bufferPool;
            _debugLogSend = debugLogSend;
            _version = version;
            _outboundEnvelopePool = outboundEnvelopePool;
            _shape = new FlowShape<IOutboundEnvelope, EnvelopeBuffer>(_in, _out);
        }

        public override FlowShape<IOutboundEnvelope, EnvelopeBuffer> Shape { get; }
        public override ILogicAndMaterializedValue<IOutboundCompressionAccess> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class Decoder
    {
        private sealed class RetryResolveRemoteDeployedRecipient
        {
            public int AttemptsLeft { get; }
            public string RecipientPath { get; }
            public IInboundEnvelope InboundEnvelope { get; }

            public RetryResolveRemoteDeployedRecipient(int attemptsLeft, string recipientPath,
                IInboundEnvelope inboundEnvelope)
            {
                AttemptsLeft = attemptsLeft;
                RecipientPath = recipientPath;
                InboundEnvelope = inboundEnvelope;
            }
        }

        private class Tick
        {
            public static Tick Instance = new Tick();
            private Tick() { }
        }

        /// <summary>
        /// Materialized value of [[Encoder]] which allows safely calling into the operator to interface with compression tables.
        /// </summary>
        public interface IInboundCompressionAccess
        {
            Task<Done> ConfirmActorRefCompressionAdvertisementAck(
                CompressionProtocol.ActorRefCompressionAdvertisementAck ack);

            Task<Done> ConfirmClassManifestCompressionAdvertisementAck(
                CompressionProtocol.ClassManifestCompressionAdvertisementAck ack);

            Task<Done> CloseCompressionFor(long originalUid);

            /// <summary>
            /// For testing purposes, usually triggered by timer from within Decoder operator.
            /// </summary>
            void RunNextActorRefAdvertisement();

            /// <summary>
            /// For testing purposes, usually triggered by timer from within Decoder operator.
            /// </summary>
            void RunNextClassManifestAdvertisement();

            /// <summary>
            /// For testing purposes
            /// </summary>
            Task<HashSet<long>> CurrentCompressionOriginUids { get; }
        }

        public abstract class InboundCompressionAccessImpl : GraphStageLogic, IInboundCompressionAccess, IStageLogging
        {
            // Timer keys
            public sealed class AdvertiseActorRefsCompressionTable
            {
                public static readonly AdvertiseActorRefsCompressionTable Instance =
                    new AdvertiseActorRefsCompressionTable();

                private AdvertiseActorRefsCompressionTable() { }
            }

            public sealed class AdvertiseClassManifestsCompressionTable
            {
                public static readonly AdvertiseClassManifestsCompressionTable Instance =
                    new AdvertiseClassManifestsCompressionTable();

                private AdvertiseClassManifestsCompressionTable() { }
            }

            public IInboundCompressions Compressions { get; }

            private readonly Action<long> CloseCompressionForCb;

            private readonly Action<CompressionProtocol.ActorRefCompressionAdvertisementAck>
                ConfirmActorRefCompressionAdvertisementCb;

            private readonly Action<CompressionProtocol.ClassManifestCompressionAdvertisementAck>
                ConfirmClassManifestCompressionAdvertisementCb;

            private readonly Action RunNextActorRefAdvertisementCb;
            private readonly Action RunNextClassManifestAdvertisementCb;
            private readonly Action<TaskCompletionSource<HashSet<long>>> CurrentCompressionOriginUidsCb;

            protected InboundCompressionAccessImpl(int inCount, int outCount) : base(inCount, outCount)
            {
                // ARTERY: Not sure if this is correct, how do we handle AsyncCallback that actually returns a Task?
                CloseCompressionForCb = GetAsyncCallback<long>(uid => Compressions.Close(uid));

                ConfirmActorRefCompressionAdvertisementCb =
                    GetAsyncCallback<CompressionProtocol.ActorRefCompressionAdvertisementAck>(
                        ack => Compressions.ConfirmActorRefCompressionAdvertisement(ack.From.Uid, ack.TableVersion));

                ConfirmClassManifestCompressionAdvertisementCb = GetAsyncCallback<CompressionProtocol.ClassManifestCompressionAdvertisementAck>(
                    ack => Compressions.ConfirmClassManifestCompressionAdvertisement(ack.From.Uid, ack.TableVersion));

                RunNextActorRefAdvertisementCb = GetAsyncCallback(() => Compressions.RunNextActorRefAdvertisement());

                RunNextClassManifestAdvertisementCb =
                    GetAsyncCallback(() => Compressions.RunNextClassManifestAdvertisement());

                CurrentCompressionOriginUidsCb = GetAsyncCallback<TaskCompletionSource<HashSet<long>>>(
                    p => p.SetResult(Compressions.CurrentOriginUids));
            }

            protected InboundCompressionAccessImpl(Shape shape) : base(shape)
            {
                CloseCompressionForCb = GetAsyncCallback<long>(uid => Compressions.Close(uid));

                ConfirmActorRefCompressionAdvertisementCb =
                    GetAsyncCallback<CompressionProtocol.ActorRefCompressionAdvertisementAck>(
                        ack => Compressions.ConfirmActorRefCompressionAdvertisement(ack.From.Uid, ack.TableVersion));

                ConfirmClassManifestCompressionAdvertisementCb = GetAsyncCallback<CompressionProtocol.ClassManifestCompressionAdvertisementAck>(
                    ack => Compressions.ConfirmClassManifestCompressionAdvertisement(ack.From.Uid, ack.TableVersion));

                RunNextActorRefAdvertisementCb = GetAsyncCallback(() => Compressions.RunNextActorRefAdvertisement());

                RunNextClassManifestAdvertisementCb =
                    GetAsyncCallback(() => Compressions.RunNextClassManifestAdvertisement());

                CurrentCompressionOriginUidsCb = GetAsyncCallback<TaskCompletionSource<HashSet<long>>>(
                    p => p.SetResult(Compressions.CurrentOriginUids));
            }

            /// <summary>
            /// External call from ChangeInboundCompression materialized value
            /// </summary>
            /// <param name="originalUid"></param>
            /// <returns></returns>
            public Task<Done> CloseCompressionFor(long originalUid)
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// External call from ChangeInboundCompression materialized value
            /// </summary>
            /// <param name="ack"></param>
            /// <returns></returns>
            public Task<Done> ConfirmActorRefCompressionAdvertisementAck(CompressionProtocol.ActorRefCompressionAdvertisementAck ack)
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// External call from ChangeInboundCompression materialized value
            /// </summary>
            /// <param name="ack"></param>
            /// <returns></returns>
            public Task<Done> ConfirmClassManifestCompressionAdvertisementAck(CompressionProtocol.ClassManifestCompressionAdvertisementAck ack)
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// External call from ChangeInboundCompression materialized value
            /// </summary>
            public void RunNextActorRefAdvertisement()
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// External call from ChangeInboundCompression materialized value
            /// </summary>
            public void RunNextClassManifestAdvertisement()
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// External call from ChangeInboundCompression materialized value
            /// </summary>
            public Task<HashSet<long>> CurrentCompressionOriginUids {
                get
                {
                    throw new NotImplementedException();
                }
            }
        }
    }

    internal sealed class ActorRefResolveCacheWithAddress : AbstractActorRefResolveCache<IInternalActorRef>
    {
        private readonly IRemoteActorRefProvider _provider;
        private readonly UniqueAddress _localAddress;

        public ActorRefResolveCacheWithAddress(
            IRemoteActorRefProvider provider, 
            UniqueAddress localAddress)
        {
            _provider = provider;
            _localAddress = localAddress;
        }

        protected override IInternalActorRef Compute(string k)
        {
            return _provider.ResolveActorRefWithLocalAddress(k, _localAddress.Address);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class Deserializer : GraphStage<FlowShape<IInboundEnvelope, IInboundEnvelope>>
    {
        private readonly IInboundContext _inboundContext;
        private readonly ExtendedActorSystem _system;
        private readonly EnvelopeBufferPool _bufferPool;

        public readonly Inlet<IInboundEnvelope> In = new Inlet<IInboundEnvelope>("Artery.Deserializer.In");
        public readonly Outlet<IInboundEnvelope> Out = new Outlet<IInboundEnvelope>("Artery.Deserializer.Out");
        public override FlowShape<IInboundEnvelope, IInboundEnvelope> Shape { get; }

        public Deserializer(IInboundContext inboundContext, ExtendedActorSystem system, EnvelopeBufferPool bufferPool)
        {
            _inboundContext = inboundContext;
            _system = system;
            _bufferPool = bufferPool;
            Shape = new FlowShape<IInboundEnvelope, IInboundEnvelope>(In, Out);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// The HandshakeReq message must be passed in each inbound lane to
    /// ensure that it arrives before any application message. Otherwise there is a risk
    /// that an application message arrives in the InboundHandshake operator before the
    /// handshake is completed and then it would be dropped.
    /// </summary>
    internal class DuplicateHandshakeReq : GraphStage<FlowShape<IInboundEnvelope, IInboundEnvelope>>
    {
        private readonly int _numberOfLanes;
        private readonly IInboundContext _inboundContext;
        private readonly ExtendedActorSystem _system;
        private readonly EnvelopeBufferPool _bufferPool;

        public readonly Inlet<IInboundEnvelope> In = new Inlet<IInboundEnvelope>("Artery.Deserializer.In");
        public readonly Outlet<IInboundEnvelope> Out = new Outlet<IInboundEnvelope>("Artery.Deserializer.Out");
        public override FlowShape<IInboundEnvelope, IInboundEnvelope> Shape { get; }

        public DuplicateHandshakeReq(
            int numberOfLanes, 
            IInboundContext inboundContext, 
            ExtendedActorSystem system, 
            EnvelopeBufferPool bufferPool)
        {
            _numberOfLanes = numberOfLanes;
            _inboundContext = inboundContext;
            _system = system;
            _bufferPool = bufferPool;

            Shape = new FlowShape<IInboundEnvelope, IInboundEnvelope>(In, Out);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            throw new NotImplementedException();
        }
    }
}
