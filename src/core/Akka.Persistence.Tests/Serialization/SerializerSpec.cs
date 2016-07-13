//-----------------------------------------------------------------------
// <copyright file="SerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests.Serialization
{
    internal class Configs
    {
        public static readonly Config CustomSerializers = ConfigurationFactory.ParseString(@"
akka.actor {
  serializers {
    my-payload = ""Akka.Persistence.Tests.Serialization.MyPayloadSerializer, Akka.Persistence.Tests""
    my-payload2 = ""Akka.Persistence.Tests.Serialization.MyPayload2Serializer, Akka.Persistence.Tests""
    my-snapshot = ""Akka.Persistence.Tests.Serialization.MySnapshotSerializer, Akka.Persistence.Tests""
    my-snapshot2 = ""Akka.Persistence.Tests.Serialization.MySnapshotSerializer2, Akka.Persistence.Tests""
    old-payload = ""Akka.Persistence.Tests.Serialization.OldPayloadSerializer, Akka.Persistence.Tests""
  }
  serialization-bindings {
    ""Akka.Persistence.Tests.Serialization.MyPayload, Akka.Persistence.Tests"" = my-payload
    ""Akka.Persistence.Tests.Serialization.MyPayload2, Akka.Persistence.Tests"" = my-payload2
    ""Akka.Persistence.Tests.Serialization.MySnapshot, Akka.Persistence.Tests"" = my-snapshot
    ""Akka.Persistence.Tests.Serialization.MySnapshot2, Akka.Persistence.Tests"" = my-snapshot2
    # this entry was used when creating the data for the test
    # ""deserialize data when class is removed""
    #""Akka.Persistence.Tests.Serialization.OldPayload, Akka.Persistence.Tests"" = old-payload
  }
}");
        public static readonly Config Remote = ConfigurationFactory.ParseString(@"
akka {
  actor {
    provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
  }
  remote {
    helios.tcp {
      transport-class = ""Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote""
	  applied-adapters = []
	  transport-protocol = tcp
	  port = 0
      hostname = ""127.0.0.1""
      port = 0
    }
  }
  loglevel = ERROR
  log-dead-letters = 0
  log-dead-letters-during-shutdown = off
}");

        public static Config Config(params string[] configs)
        {
            return configs.Aggregate(ConfigurationFactory.Empty,
                (r, c) => r.WithFallback(ConfigurationFactory.ParseString(c)));
        }
        
        public static Config Config(params Config[] configs)
        {
            return configs.Aggregate(ConfigurationFactory.Empty,
                (r, c) => r.WithFallback(c));
        }
    }

    public class MyPayload
    {
        public string Data { get; private set; }

        public MyPayload(string data)
        {
            Data = data;
        }

        protected bool Equals(MyPayload other)
        {
            return string.Equals(Data, other.Data);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((MyPayload) obj);
        }

        public override int GetHashCode()
        {
            return (Data != null ? Data.GetHashCode() : 0);
        }
    }

    public class MyPayload2
    {
        public string Data { get; private set; }
        public int N { get; private set; }

        public MyPayload2(string data, int n)
        {
            Data = data;
            N = n;
        }

        protected bool Equals(MyPayload2 other)
        {
            return string.Equals(Data, other.Data) && N == other.N;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((MyPayload2) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Data != null ? Data.GetHashCode() : 0)*397) ^ N;
            }
        }
    }

    public class MySnapshot
    {
        public string Data { get; private set; }

        public MySnapshot(string data)
        {
            Data = data;
        }

        protected bool Equals(MySnapshot other)
        {
            return string.Equals(Data, other.Data);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((MySnapshot) obj);
        }

        public override int GetHashCode()
        {
            return (Data != null ? Data.GetHashCode() : 0);
        }
    }

    public class MySnapshot2
    {
        public string Data { get; private set; }

        public MySnapshot2(string data)
        {
            Data = data;
        }

        protected bool Equals(MySnapshot2 other)
        {
            return string.Equals(Data, other.Data);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((MySnapshot2) obj);
        }

        public override int GetHashCode()
        {
            return (Data != null ? Data.GetHashCode() : 0);
        }
    }

    // this class was used when creating the data for the test
    // "deserialize data when class is removed"
    /*public class OldPayload
    {
        public char C { get; private set; }

        public OldPayload(char c)
        {
            C = c;
        }


        public override string ToString()
        {
            return string.Format("OldPayload({0})", C);
        }
    }*/

    public class SnapshotSerializerPersistenceSpec : AkkaSpec
    {
        private readonly Akka.Serialization.Serialization _serialization;
            
        public SnapshotSerializerPersistenceSpec() : base(
            Configs.Config(
                Configs.CustomSerializers,
                ConfigurationFactory.FromResource<Persistence>("Akka.Persistence.persistence.conf") // for akka-persistence-snapshot
                ))
        {
            _serialization = Sys.Serialization;
        }

        [Fact]
        public void SnapshotSerializer_should_handle_custom_snapshot_Serialization()
        {
            var wrapped = new Akka.Persistence.Serialization.Snapshot(new MySnapshot("a"));
            var serializer = _serialization.FindSerializerFor(wrapped);

            var bytes = serializer.ToBinary(wrapped);
            var deserialized = serializer.FromBinary(bytes, null);

            deserialized.ShouldBe(new Akka.Persistence.Serialization.Snapshot(new MySnapshot(".a.")));
        }

        [Fact]
        public void SnapshotSerializer_should_handle_custom_snapshot_Serialization_with_string_manifest()
        {
            var wrapped = new Akka.Persistence.Serialization.Snapshot(new MySnapshot2("a"));
            var serializer = _serialization.FindSerializerFor(wrapped);

            var bytes = serializer.ToBinary(wrapped);
            var deserialized = serializer.FromBinary(bytes, null);

            deserialized.ShouldBe(new Akka.Persistence.Serialization.Snapshot(new MySnapshot2(".a.")));
        }
    }

    public class MessageSerializerPersistenceSpec : AkkaSpec
    {
        private readonly Akka.Serialization.Serialization _serialization;

        public MessageSerializerPersistenceSpec() : base(
            Configs.Config(
                Configs.CustomSerializers,
                ConfigurationFactory.FromResource<Persistence>("Akka.Persistence.persistence.conf") // for akka-persistence-message
                ))
        {
            _serialization = Sys.Serialization;
        }

        [Fact]
        public void MessageSerializer_when_not_given_a_manifest_should_handle_custom_Persistent_message_serialization()
        {
            var persistent = new Persistent(new MyPayload("a"), 13, "p1", "", writerGuid: Guid.NewGuid().ToString());
            var serializer = _serialization.FindSerializerFor(persistent);

            var bytes = serializer.ToBinary(persistent);
            var deserialized = serializer.FromBinary(bytes, null);

            deserialized.ShouldBe(persistent.WithPayload(new MyPayload(".a.")));
        }

        [Fact]
        public void MessageSerializer_when_given_a_Persistent_manifest_should_handle_custom_Persistent_message_serialization()
        {
            var persistent = new Persistent(new MyPayload("b"), 13, "p1", "", writerGuid: Guid.NewGuid().ToString());
            var serializer = _serialization.FindSerializerFor(persistent);

            var bytes = serializer.ToBinary(persistent);
            var deserialized = serializer.FromBinary(bytes, typeof(Persistent));

            deserialized.ShouldBe(persistent.WithPayload(new MyPayload(".b.")));
        }

        [Fact]
        public void MessageSerializer_when_given_payload_serializer_with_string_manifest_should_handle_serialization()
        {
            var persistent = new Persistent(new MyPayload2("a", 17), 13, "p1", "", writerGuid: Guid.NewGuid().ToString());
            var serializer = _serialization.FindSerializerFor(persistent);

            var bytes = serializer.ToBinary(persistent);
            var deserialized = serializer.FromBinary(bytes, null);

            deserialized.ShouldBe(persistent.WithPayload(new MyPayload2(".a.", 17)));
        }

        [Fact]
        public void MessageSerializer_when_given_payload_serializer_with_string_manifest_should_be_able_to_evolve_the_data_type()
        {
            var oldEvent = new MyPayload("a");
            var serializer1 = _serialization.FindSerializerFor(oldEvent);
            var bytes = serializer1.ToBinary(oldEvent);

            // now the system is updated to version 2 with new class MyPayload2
            // and MyPayload2Serializer that handles migration from old MyPayload
            var serializer2 = _serialization.FindSerializerForType(typeof(MyPayload2));
            var deserialized = serializer2.FromBinary(bytes, oldEvent.GetType());

            deserialized.ShouldBe(new MyPayload2(".a.", 0));
        }

        [Fact]
        public void MessageSerializer_when_given_payload_serializer_with_string_manifest_should_be_able_to_deserialize_data_when_class_is_removed()
        {
            var serializer = _serialization.FindSerializerFor(new Persistent("x", 13, "p1", ""));

            // It was created with:
            // var old = new Persistent(new OldPayload('A'), 13, "p1", "");
            // var oldData = Convert.ToBase64String(serializer.ToBinary(old));
            var oldData =
                "ClsIx9oEEg1PbGRQYXlsb2FkKEEpGkZBa2thLlBlcnNpc3RlbmNlLlRlc3RzLlNlcmlhbGl6YXRpb24uT2xkUGF5bG9hZCxBa2thLlBlcnNpc3RlbmNlLlRlc3RzEA0aAnAxYgBqAA==";

            // now the system is updated, OldPayload is replaced by MyPayload, and the
            // and OldPayloadSerializer is adjusted to migrate OldPayload
            var bytes = Convert.FromBase64String(oldData);

            var deserialized = (Persistent)serializer.FromBinary(bytes, null);

            deserialized.Payload.ShouldBe(new MyPayload("OldPayload(A)"));
        }

        [Fact]
        public void MessageSerializer_when_given_AtLeastOnceDeliverySnapshot_should_handle_empty_unconfirmed()
        {
            var unconfirmed = new UnconfirmedDelivery[0];
            var snap = new AtLeastOnceDeliverySnapshot(13, unconfirmed);
            var serializer = _serialization.FindSerializerFor(snap);

            var bytes = serializer.ToBinary(snap);
            var deserialized = serializer.FromBinary(bytes, typeof (AtLeastOnceDeliverySnapshot));

            deserialized.ShouldBe(snap);
        }

        [Fact]
        public void MessageSerializer_when_given_AtLeastOnceDeliverySnapshot_should_handle_a_few_unconfirmed()
        {
            var unconfirmed = new[]
            {
                new UnconfirmedDelivery(1, TestActor.Path, "a"),
                new UnconfirmedDelivery(2, TestActor.Path, "b"),
                new UnconfirmedDelivery(3, TestActor.Path, 42),
            };
            var snap = new AtLeastOnceDeliverySnapshot(17, unconfirmed);
            var serializer = _serialization.FindSerializerFor(snap);

            var bytes = serializer.ToBinary(snap);
            var deserialized = serializer.FromBinary(bytes, typeof (AtLeastOnceDeliverySnapshot));

            deserialized.ShouldBe(snap);
        }
    }

#if !CORECLR
    public class MessageSerializerRemotingSpec : AkkaSpec
    {
        internal class LocalActor : ActorBase
        {
            private readonly int _port;

            public LocalActor(int port)
            {
                _port = port;
            }

            protected override bool Receive(object message)
            {
                Context.ActorSelection(string.Format("akka.tcp://remote@127.0.0.1:{0}/user/remote", _port))
                    .Tell(message, ActorRefs.NoSender);
                return true;
            }
        }

        internal class RemoteActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                if (message is Persistent)
                {
                    var p = (Persistent) message;
                    if (p.Payload is MyPayload)
                    {
                        p.Sender.Tell("p" + ((MyPayload) p.Payload).Data);
                    }
                    else return false;
                }
                else if (message is AtomicWrite)
                {
                    var a = (AtomicWrite) message;
                    foreach (var p in (IEnumerable<IPersistentRepresentation>) a.Payload)
                    {
                        if (p.Payload is MyPayload)
                        {
                            p.Sender.Tell("p" + ((MyPayload) p.Payload).Data);
                        }
                    }
                }
                else return false;
                return true;
            }
        }

        private readonly ActorSystem _remoteSystem;
        private readonly IActorRef _localActor;

        private readonly Akka.Serialization.Serialization _serialization;

        public MessageSerializerRemotingSpec() : base(
            Configs.Config(
                Configs.Remote,
                Configs.CustomSerializers,
                ConfigurationFactory.FromResource<Persistence>("Akka.Persistence.persistence.conf") // for akka-persistence-message
                ))
        {
            _remoteSystem = ActorSystem.Create("remote",
                Configs.Remote.WithFallback(Configs.CustomSerializers)
                    .WithFallback(ConfigurationFactory.FromResource<Persistence>("Akka.Persistence.persistence.conf")));
            _localActor = Sys.ActorOf(Props.Create(() => new LocalActor(Port(_remoteSystem))), "local");

            _serialization = Sys.Serialization;

            _remoteSystem.ActorOf(Props.Create(() => new RemoteActor()), "remote");
        }

        private int Port(ActorSystem system)
        {
            return Address(system).Port.Value;
        }

        private Address Address(ActorSystem system)
        {
            return ((ExtendedActorSystem) system).Provider.DefaultAddress;
        }

        protected override void AfterTermination()
        {
            _remoteSystem.Terminate().Wait(TimeSpan.FromSeconds(2));
            base.AfterTermination();
        }

        [Fact]
        public void MessageSerializer_should_custom_serialize_Persistent_messages_during_remoting()
        {
            // this also verifies serialization of Persistent.Sender,
            // because the RemoteActor will reply to the Persistent.Sender
            _localActor.Tell(new Persistent(new MyPayload("a"), sender: TestActor));
            ExpectMsg("p.a.");
        }

        [Fact]
        public void MessageSerializer_should_custom_serialize_AtomicWrite_messages_during_remoting()
        {
            var p1 = new Persistent(new MyPayload("a"), sender: TestActor);
            var p2 = new Persistent(new MyPayload("b"), sender: TestActor);
            _localActor.Tell(new AtomicWrite(ImmutableList.Create(new IPersistentRepresentation[] {p1, p2})));
            ExpectMsg("p.a.");
            ExpectMsg("p.b.");
        }

        [Fact]
        public void MessageSerializer_should_serialize_manifest_provided_by_EventAdapter()
        {
            var p1 = new Persistent(new MyPayload("a"), sender: TestActor).WithManifest("manifest");
            var serializer = _serialization.FindSerializerFor(p1);
            var bytes = serializer.ToBinary(p1);
            var back = (Persistent)serializer.FromBinary(bytes, typeof (Persistent));

            back.Manifest.ShouldBe(p1.Manifest);
        }
    }
#endif
}