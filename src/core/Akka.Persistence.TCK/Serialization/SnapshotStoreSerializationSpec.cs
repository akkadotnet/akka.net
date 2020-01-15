//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Fsm;
using Akka.Serialization;
using Xunit;
using Xunit.Abstractions;
using Akka.Util.Internal;

namespace Akka.Persistence.TCK.Serialization
{
    public abstract class SnapshotStoreSerializationSpec : PluginSpec
    {
        protected SnapshotStoreSerializationSpec(Config config, string actorSystem, ITestOutputHelper output) 
            : base(ConfigurationFactory.ParseString(@"
                akka.actor {
                  serializers {
                    my-snapshot = ""Akka.Persistence.TCK.Serialization.Test+MySnapshotSerializer, Akka.Persistence.TCK""
                    my-snapshot2 = ""Akka.Persistence.TCK.Serialization.Test+MySnapshotSerializer2, Akka.Persistence.TCK""
                  }
                  serialization-bindings {
                    ""Akka.Persistence.TCK.Serialization.Test+MySnapshot, Akka.Persistence.TCK"" = my-snapshot
                    ""Akka.Persistence.TCK.Serialization.Test+MySnapshot2, Akka.Persistence.TCK"" = my-snapshot2
                  }
                }
            ").WithFallback(config), actorSystem, output)
        {
        }

        protected IActorRef SnapshotStore => Extension.SnapshotStoreFor(null);

        [Fact]
        public virtual void SnapshotStore_should_serialize_Payload()
        {
            var probe = CreateTestProbe();

            var snapshot = new Test.MySnapshot("a");

            var metadata = new SnapshotMetadata(Pid, 1);
            SnapshotStore.Tell(new SaveSnapshot(metadata, snapshot), probe.Ref);
            probe.ExpectMsg<SaveSnapshotSuccess>();

            SnapshotStore.Tell(new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);
            probe.ExpectMsg<LoadSnapshotResult>(s => s.Snapshot.Snapshot is Test.MySnapshot
                && s.Snapshot.Snapshot.AsInstanceOf<Test.MySnapshot>().Data.Equals(".a."));
        }

        [Fact]
        public virtual void SnapshotStore_should_serialize_Payload_with_string_manifest()
        {
            var probe = CreateTestProbe();

            var snapshot = new Test.MySnapshot2("a");

            var metadata = new SnapshotMetadata(Pid, 1);
            SnapshotStore.Tell(new SaveSnapshot(metadata, snapshot), probe.Ref);
            probe.ExpectMsg<SaveSnapshotSuccess>();

            SnapshotStore.Tell(new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);
            probe.ExpectMsg<LoadSnapshotResult>(s => s.Snapshot.Snapshot is Test.MySnapshot2
                && s.Snapshot.Snapshot.AsInstanceOf<Test.MySnapshot2>().Data.Equals(".a."));
        }

        [Fact]
        public virtual void SnapshotStore_should_serialize_AtLeastOnceDeliverySnapshot()
        {
            var probe = CreateTestProbe();

            var unconfirmed = new UnconfirmedDelivery[]
            {
                new UnconfirmedDelivery(1, TestActor.Path, "a"),
                new UnconfirmedDelivery(2, TestActor.Path, "b"),
                new UnconfirmedDelivery(3, TestActor.Path, 42)
            };
            var atLeastOnceDeliverySnapshot = new AtLeastOnceDeliverySnapshot(17, unconfirmed);

            var metadata = new SnapshotMetadata(Pid, 2);
            SnapshotStore.Tell(new SaveSnapshot(metadata, atLeastOnceDeliverySnapshot), probe.Ref);
            probe.ExpectMsg<SaveSnapshotSuccess>();

            SnapshotStore.Tell(new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);
            probe.ExpectMsg<LoadSnapshotResult>(s => s.Snapshot.Snapshot.Equals(atLeastOnceDeliverySnapshot));
        }

        [Fact]
        public virtual void SnapshotStore_should_serialize_AtLeastOnceDeliverySnapshot_with_empty_unconfirmed()
        {
            var probe = CreateTestProbe();

            var unconfirmed = new UnconfirmedDelivery[0];
            var atLeastOnceDeliverySnapshot = new AtLeastOnceDeliverySnapshot(13, unconfirmed);

            var metadata = new SnapshotMetadata(Pid, 2);
            SnapshotStore.Tell(new SaveSnapshot(metadata, atLeastOnceDeliverySnapshot), probe.Ref);
            probe.ExpectMsg<SaveSnapshotSuccess>();

            SnapshotStore.Tell(new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);
            probe.ExpectMsg<LoadSnapshotResult>(s => s.Snapshot.Snapshot.Equals(atLeastOnceDeliverySnapshot));
        }

        [Fact]
        public virtual void SnapshotStore_should_serialize_PersistentFSMSnapshot()
        {
            var probe = CreateTestProbe();

            var persistentFSMSnapshot = new PersistentFSM.PersistentFSMSnapshot<string>("mystate", "mydata", TimeSpan.FromDays(4));

            var metadata = new SnapshotMetadata(Pid, 2);
            SnapshotStore.Tell(new SaveSnapshot(metadata, persistentFSMSnapshot), probe.Ref);
            probe.ExpectMsg<SaveSnapshotSuccess>();

            SnapshotStore.Tell(new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);
            probe.ExpectMsg<LoadSnapshotResult>(s => s.Snapshot.Snapshot.Equals(persistentFSMSnapshot));
        }
    }

    internal static class Test
    {
        public class MySnapshot
        {
            public MySnapshot(string data)
            {
                Data = data;
            }

            public string Data { get; }
        }

        public class MySnapshot2
        {
            public MySnapshot2(string data)
            {
                Data = data;
            }

            public string Data { get; }
        }

        public class MySnapshotSerializer : Serializer
        {
            public MySnapshotSerializer(ExtendedActorSystem system) : base(system) { }
            public override int Identifier => 77124;
            public override bool IncludeManifest => true;

            public override byte[] ToBinary(object obj)
            {
                if (obj is MySnapshot snapshot) return Encoding.UTF8.GetBytes($".{snapshot.Data}");
                throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{nameof(MySnapshotSerializer2)}]");
            }

            public override object FromBinary(byte[] bytes, Type type)
            {
                if (type == typeof(MySnapshot)) return new MySnapshot($"{Encoding.UTF8.GetString(bytes)}.");
                throw new ArgumentException($"Unimplemented deserialization of message with manifest [{type}] in serializer {nameof(MySnapshotSerializer)}");
            }
        }

        public class MySnapshotSerializer2 : SerializerWithStringManifest
        {
            private const string ContactsManifest = "A";

            public MySnapshotSerializer2(ExtendedActorSystem system) : base(system) { }
            public override int Identifier => 77126;

            public override byte[] ToBinary(object obj)
            {
                if (obj is MySnapshot2 snapshot) return Encoding.UTF8.GetBytes($".{snapshot.Data}");
                throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{nameof(MySnapshotSerializer2)}]");
            }

            public override string Manifest(object obj)
            {
                if (obj is MySnapshot2) return ContactsManifest;
                throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{nameof(MySnapshotSerializer2)}]");
            }

            public override object FromBinary(byte[] bytes, string manifest)
            {
                if (manifest == ContactsManifest) return new MySnapshot2(Encoding.UTF8.GetString(bytes) + ".");
                throw new ArgumentException($"Unimplemented deserialization of message with manifest [{manifest}] in serializer {nameof(MySnapshotSerializer2)}");
            }
        }
    }
}
