using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;

namespace Akka.Remote.Artery.Compress
{
    internal static class CompressionProtocol
    {
        /// <summary>
        /// INTERNAL API
        /// </summary>
        public interface ICompressionMessage { }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        public interface ICompressionAckMessage : ICompressionMessage { }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        public interface ICompressionAdvertisement<T> : IControlMessage, ICompressionMessage
        {
            UniqueAddress From { get; }
            CompressionTable<T> Table { get; }
        }

        /// <summary>
        /// INTERNAL API
        /// Sent by the "receiving" node after allocating a compression id to a given [[akka.actor.ActorRef]]
        /// </summary>
        internal sealed class ActorRefCompressionAdvertisement : ICompressionAdvertisement<IActorRef>
        {
            public UniqueAddress From { get; }
            public CompressionTable<IActorRef> Table { get; }

            public ActorRefCompressionAdvertisement(UniqueAddress from, CompressionTable<IActorRef> table)
            {
                From = from;
                Table = table;
            }
        }

        /// <summary>
        /// INTERNAL API
        /// Sent by the "sending" node after receiving <see cref="ActorRefCompressionAdvertisement"/> 
        /// The advertisement is also confirmed by the first message using that table version,
        /// but we need separate ack in case the sender is not using any of the refs in the advertised
        /// table.
        /// </summary>
        internal sealed class ActorRefCompressionAdvertisementAck : IControlMessage, ICompressionAckMessage
        {
            public UniqueAddress From { get; }
            public byte TableVersion { get; }

            public ActorRefCompressionAdvertisementAck(UniqueAddress from, byte tableVersion)
            {
                From = from;
                TableVersion = tableVersion;
            }
        }

        /// <summary>
        /// INTERNAL API
        /// Sent by the "receiving" node after allocating a compression id to a given class manifest
        /// </summary>
        internal sealed class ClassManifestCompressionAdvertisement : ICompressionAdvertisement<string>
        {
            public UniqueAddress From { get; }
            public CompressionTable<string> Table { get; }

            public ClassManifestCompressionAdvertisement(UniqueAddress from, CompressionTable<string> table)
            {
                From = from;
                Table = table;
            }
        }

        /// <summary>
        /// INTERNAL API
        /// Sent by the "sending" node after receiving <see cref="ClassManifestCompressionAdvertisement"/>
        /// The advertisement is also confirmed by the first message using that table version,
        /// but we need separate ack in case the sender is not using any of the refs in the advertised
        /// table.
        /// </summary>
        internal sealed class ClassManifestCompressionAdvertisementAck : IControlMessage, ICompressionAckMessage
        {
            public UniqueAddress From { get; }
            public byte TableVersion { get; }

            public ClassManifestCompressionAdvertisementAck(UniqueAddress from, byte tableVersion)
            {
                From = from;
                TableVersion = tableVersion;
            }
        }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        internal static class Events
        {
            internal interface IEvent { }

            internal sealed class HeavyHitterDetected : IEvent
            {
                public object Key { get; }
                public int Id { get; }
                public long Count { get; }

                public HeavyHitterDetected(object key, int id, long count)
                {
                    Key = key;
                    Id = id;
                    Count = count;
                }
            }

            internal sealed class ReceivedActorRefCompressionTable : IEvent
            {
                public UniqueAddress From { get; }
                public CompressionTable<IActorRef> Table { get; }

                public ReceivedActorRefCompressionTable(UniqueAddress from, CompressionTable<IActorRef> table)
                {
                    From = from;
                    Table = table;
                }
            }

            internal sealed class ReceivedClassManifestCompressionTable : IEvent
            {
                public UniqueAddress From { get; }
                public CompressionTable<string> Table { get; }

                public ReceivedClassManifestCompressionTable(UniqueAddress from, CompressionTable<string> table)
                {
                    From = from;
                    Table = table;
                }
            }
        }
    }
}
