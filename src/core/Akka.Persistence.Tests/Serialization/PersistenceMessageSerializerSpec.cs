//-----------------------------------------------------------------------
// <copyright file="PersistenceMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Fsm;
using Akka.Persistence.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Tests.Serialization
{
    public class PersistenceMessageSerializerSpec : AkkaSpec
    {
        public static readonly Config CustomSerializers = @"
            akka.actor {
              serializers {
                my-payload = ""Akka.Persistence.Tests.Serialization.MyPayloadSerializer, Akka.Persistence.Tests""
              }
              serialization-bindings {
                ""Akka.Persistence.Tests.Serialization.MyPayload, Akka.Persistence.Tests"" = my-payload
              }
            }";

        private readonly PersistenceMessageSerializer _serializer;

        public PersistenceMessageSerializerSpec() : base(CustomSerializers)
        {
            _serializer = new PersistenceMessageSerializer(Sys.As<ExtendedActorSystem>());
        }

        [Fact]
        public void MessageSerializer_should_serialize_manifest_provided_by_EventAdapter()
        {
            var p1 = new Persistent(new MyPayload("a"), sender: TestActor).WithManifest("manifest");
            var bytes = _serializer.ToBinary(p1);
            var back = _serializer.FromBinary<Persistent>(bytes);

            back.Manifest.Should().Be(p1.Manifest);
        }

        [Fact]
        public void MessageSerializer_should_serialize_state_change_event()
        {
            var p1 = new Persistent(new PersistentFSM.StateChangeEvent("a", TimeSpan.FromSeconds(10)), sender: TestActor);
            var bytes = _serializer.ToBinary(p1);
            var back = _serializer.FromBinary<Persistent>(bytes);
            var payload = back.Payload as PersistentFSM.StateChangeEvent;
            payload.Should().NotBeNull();
            payload.StateIdentifier.Should().Be("a");
            payload.Timeout.Should().Be(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void MessageSerializer_should_serialize_AtLeastOnceDeliverySnapshot()
        {
            var unconfirmed = new[]
            {
                new UnconfirmedDelivery(1, TestActor.Path, "a"),
                new UnconfirmedDelivery(2, TestActor.Path, "b"),
                new UnconfirmedDelivery(3, TestActor.Path, "big string")
            };
            var atLeastOnceDeliverySnapshot = new AtLeastOnceDeliverySnapshot(17, unconfirmed);

            var bytes = _serializer.ToBinary(atLeastOnceDeliverySnapshot);
            var backSnapshot = _serializer.FromBinary<AtLeastOnceDeliverySnapshot>(bytes);
            backSnapshot.Should().NotBeNull();
            backSnapshot.Should().Be(atLeastOnceDeliverySnapshot);
        }

        [Fact]
        public void MessageSerializer_should_serialize_fsm_snapshot()
        {
            var snapshot = new PersistentFSM.PersistentFSMSnapshot<MyPayload>("a", new MyPayload("b"), TimeSpan.FromSeconds(10));
            var bytes = _serializer.ToBinary(snapshot);
            var backSnapshot = _serializer.FromBinary<PersistentFSM.PersistentFSMSnapshot<MyPayload>>(bytes);
            backSnapshot.Should().NotBeNull();
            backSnapshot.StateIdentifier.Should().Be("a");
            backSnapshot.Data.Data.Should().Be(".b.");    // custom MyPayload serializer prepends and appends .
            backSnapshot.Timeout.Should().Be(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void MessageSerializer_ToBinary_should_throw_an_exception_on_wrong_type()
        {
            Action serializeAction = () => _serializer.ToBinary("non supporter string type");
            serializeAction.ShouldThrow<ArgumentException>()
                .WithMessage($"Can't serialize object of type [{typeof(string)}] in [{typeof(PersistenceMessageSerializer)}]");

            Action deserializeAction = () =>
            {
                _serializer.FromBinary<string>(new byte[] { 4, 5, 6 });
            };

            deserializeAction.ShouldThrow<SerializationException>()
                .WithMessage($"Unimplemented deserialization of message with type [{typeof(string)}] in [{typeof(PersistenceMessageSerializer)}]");
        }
    }
}
