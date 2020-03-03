//-----------------------------------------------------------------------
// <copyright file="SnapshotSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Xunit;
using System.IO;
using Akka.Serialization;

namespace Akka.Persistence.Tests
{
    public class SnapshotSerializationSpec : PersistenceSpec
    {
        public interface ISerializationMarker
        {
            
        }

        public class
            SnapshotTypeWithAFullyQualifiedNameLongerThan255BytesXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
            : ISerializationMarker
        {
            public
                SnapshotTypeWithAFullyQualifiedNameLongerThan255BytesXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
                (string id)
            {
                Id = id;
            }

            public string Id { get; private set; }

            protected bool Equals(
                SnapshotTypeWithAFullyQualifiedNameLongerThan255BytesXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
                    other)
            {
                return string.Equals(Id, other.Id);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return
                    Equals(
                        (
                            SnapshotTypeWithAFullyQualifiedNameLongerThan255BytesXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
                            ) obj);
            }

            public override int GetHashCode()
            {
                return (Id != null ? Id.GetHashCode() : 0);
            }
        }

        public class MySerializer : Serializer
        {
            public MySerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override bool IncludeManifest { get { return true; } }
            public override int Identifier { get { return 5177; } }

            public override byte[] ToBinary(object obj)
            {
                using (var bStream = new MemoryStream())
                {
                    using (var writer = new StreamWriter(bStream))
                    {
                        var msg =
                            obj is
                                SnapshotTypeWithAFullyQualifiedNameLongerThan255BytesXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
                                ? ((
                                    SnapshotTypeWithAFullyQualifiedNameLongerThan255BytesXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
                                    ) obj).Id
                                : "unknown";
                        writer.Write(msg);
                        writer.Flush();
                        return bStream.ToArray();
                    }
                }
            }

            public override object FromBinary(byte[] bytes, Type type)
            {
                using (var bStream = new MemoryStream(bytes))
                {
                    using (var reader = new StreamReader(bStream))
                    {
                        return new SnapshotTypeWithAFullyQualifiedNameLongerThan255BytesXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx(reader.ReadLine());
                    }
                }
            }
        }

        internal class TestPersistentActor : NamedPersistentActor
        {
            private readonly IActorRef _probe;

            public TestPersistentActor(string name, IActorRef probe) : base(name)
            {
                _probe = probe;
            }


            protected override bool ReceiveRecover(object message)
            {
                if (message is RecoveryCompleted)
                {
                    // ignore
                }
                else
                    _probe.Tell(message);
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is string)
                    SaveSnapshot(new SnapshotTypeWithAFullyQualifiedNameLongerThan255BytesXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx(
                        (string)message));
                else if (message is SaveSnapshotSuccess)
                    _probe.Tell(((SaveSnapshotSuccess)message).Metadata.SequenceNr);
                else
                    _probe.Tell(message);
                return true;
            }
        }

        public SnapshotSerializationSpec() : base(Configuration("SnapshotSerializationSpec", serialization: "off", extraConfig:
            @"
    akka.actor {
      serializers {
        my-snapshot = ""Akka.Persistence.Tests.SnapshotSerializationSpec+MySerializer, Akka.Persistence.Tests""
      }
      serialization-bindings {
        ""Akka.Persistence.Tests.SnapshotSerializationSpec+ISerializationMarker, Akka.Persistence.Tests"" = my-snapshot
      }
    }")) { }

        [Fact]
        public void PersistentActor_with_custom_Serializer_should_be_able_to_handle_serialization_header_of_more_than_255_bytes()
        {
            var spref = Sys.ActorOf(Props.Create(() => new TestPersistentActor(Name, TestActor)));
            var persistenceId = Name;

            spref.Tell("blahonga");
            ExpectMsg(0L);

            var lpref = Sys.ActorOf(Props.Create(() => new TestPersistentActor(Name, TestActor)));
            ExpectMsg<SnapshotOffer>(m => m.Metadata.PersistenceId.Equals(persistenceId) &&
            m.Metadata.SequenceNr == 0L &&
            m.Metadata.Timestamp > SnapshotMetadata.TimestampNotSpecified &&
            m.Snapshot.Equals(new SnapshotTypeWithAFullyQualifiedNameLongerThan255BytesXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx(
                "blahonga")));
        }
    }
}
