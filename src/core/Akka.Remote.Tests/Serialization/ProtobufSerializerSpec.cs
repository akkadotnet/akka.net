//-----------------------------------------------------------------------
// <copyright file="ProtobufSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Pattern;
using Akka.Remote.Configuration;
using Akka.Remote.Serialization;
using Akka.Remote.Serialization.Proto.Msg;
using Akka.TestKit;
using Akka.Util;
using Akka.Util.Reflection;
using FluentAssertions;
using Google.Protobuf.Reflection;
using Xunit;

namespace Akka.Remote.Tests.Serialization
{
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class ProtobufSerializerSpec : AkkaSpec
    {
        public ProtobufSerializerSpec(ITestOutputHelper output) : base(
            BootstrapSetup.Create().WithConfig(RemoteConfigFactory.Default())
                .And(ProtobufSerializerSetup.Create(RegisteredMessage.Descriptor)), output)
        {
        }

        [Fact(DisplayName = "ProtobufSerializer should preserve the existing typed deserialization path")]
        public void Can_serialize_ProtobufMessage()
        {
            var message = new AddressData
            {
                System = "sys",
                Hostname = "localhost",
                Protocol = "akka.tcp",
                Port = 54645
            };
            var serializer = Sys.Serialization.FindSerializerFor(message);
            serializer.Should().BeOfType<ProtobufSerializer>();
            serializer.FromBinary(serializer.ToBinary(message), typeof(AddressData)).Should().Be(message);
        }

        [Theory(DisplayName = "ProtobufSerializer should resolve registered CLR manifests when dynamic type loading is off")]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task Should_parse_registered_messages_When_dynamic_type_loading_is_disabled(int spelling)
        {
            await WithSwitchAsync(false, () =>
            {
                TypeCache.Clear();
                var message = new RegisteredMessage { Text = "registered" };
                var serializer = Sys.Serialization.FindSerializerFor(message);
                serializer.Identifier.Should().Be(2);
                serializer.Manifest(message).Should().Be(typeof(RegisteredMessage).TypeQualifiedName());
                var manifest = spelling switch
                {
                    0 => serializer.Manifest(message),
                    1 => typeof(RegisteredMessage).FullName!,
                    _ => typeof(RegisteredMessage).AssemblyQualifiedName!
                };
                var bytes = serializer.ToBinary(message);
                Sys.Serialization.Deserialize(bytes, serializer.Identifier, manifest).Should().Be(message);
                serializer.FromBinary(bytes, typeof(RegisteredMessage)).Should().Be(message);
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "ProtobufSerializer should reject unregistered messages even when reflection caches are warm")]
        public async Task Should_reject_unregistered_messages_When_dynamic_type_loading_is_disabled()
        {
            TypeCache.Clear();
            var message = new UnregisteredMessage { Value = 42 };
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var bytes = serializer.ToBinary(message);
            var manifest = serializer.Manifest(message);
            await WithSwitchAsync(true, () =>
            {
                Sys.Serialization.Deserialize(bytes, serializer.Identifier, manifest).Should().Be(message);
                return Task.CompletedTask;
            });
            await WithSwitchAsync(false, () =>
            {
                Action deserialize = () => Sys.Serialization.Deserialize(bytes, serializer.Identifier, manifest);
                deserialize.Should().Throw<SerializationException>().WithMessage("*Akka.DynamicTypeLoading off*ProtobufSerializerSetup*");
                Action deserializeType = () => serializer.FromBinary(bytes, typeof(UnregisteredMessage));
                deserializeType.Should().Throw<SerializationException>().WithMessage("*ProtobufSerializerSetup*");
                TypeCache.Clear();
                deserialize.Should().Throw<SerializationException>().WithMessage("*ProtobufSerializerSetup*");
                return Task.CompletedTask;
            });
        }

        [Theory(DisplayName = "ProtobufSerializer should use reflection for an unregistered system only with dynamic type loading on")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_keep_registration_local_When_another_system_has_no_setup(bool dynamicTypeLoading)
        {
            await WithSwitchAsync(dynamicTypeLoading, async () =>
            {
                TypeCache.Clear();
                using var other = ActorSystem.Create("protobuf-unregistered", RemoteConfigFactory.Default());
                try
                {
                    var message = new RegisteredMessage { Text = "local" };
                    var serializer = Sys.Serialization.FindSerializerFor(message);
                    var bytes = serializer.ToBinary(message);
                    var manifest = serializer.Manifest(message);
                    Sys.Serialization.Deserialize(bytes, serializer.Identifier, manifest).Should().Be(message);
                    Action deserialize = () => other.Serialization.Deserialize(bytes, serializer.Identifier, manifest);
                    if (dynamicTypeLoading)
                        other.Serialization.Deserialize(bytes, serializer.Identifier, manifest).Should().Be(message);
                    else
                        deserialize.Should().Throw<SerializationException>().WithMessage("*ProtobufSerializerSetup*");
                }
                finally
                {
                    await other.Terminate();
                }
            });
        }

        [Fact(DisplayName = "Registered Protobuf messages should round-trip over remoting with dynamic type loading off")]
        public async Task Should_round_trip_over_remoting_When_dynamic_type_loading_is_disabled()
        {
            await WithSwitchAsync(false, async () =>
            {
                TypeCache.Clear();
                var config = ConfigurationFactory.ParseString(@"
                    akka.actor.provider = remote
                    akka.remote.dot-netty.tcp { hostname = 127.0.0.1, port = 0 }");
                var setup = BootstrapSetup.Create().WithConfig(config)
                    .And(ProtobufSerializerSetup.Create(RegisteredMessage.Descriptor));
                using var sender = ActorSystem.Create("protobuf-sender", setup);
                using var receiver = ActorSystem.Create("protobuf-receiver", setup);
                try
                {
                    var echo = receiver.ActorOf(Props.Create(() => new ProtobufEchoActor()), "echo");
                    var path = echo.Path.ToStringWithAddress(RARP.For(receiver).Provider.DefaultAddress);
                    var remote = await sender.ActorSelection(path).ResolveOne(TimeSpan.FromSeconds(10));
                    var message = new RegisteredMessage { Text = "over the wire" };
                    (await remote.Ask<RegisteredMessage>(message, TimeSpan.FromSeconds(10))).Should().Be(message);
                }
                finally
                {
                    await Task.WhenAll(sender.Terminate(), receiver.Terminate());
                }
            });
        }

        [Fact(DisplayName = "ProtobufSerializerSetup should validate and copy its descriptor inputs")]
        public void Should_validate_and_copy_descriptors_When_creating_setup()
        {
            Action nullArray = () => ProtobufSerializerSetup.Create(null!);
            nullArray.Should().Throw<ArgumentNullException>();
            Action nullDescriptor = () => ProtobufSerializerSetup.Create(new MessageDescriptor[] { null! });
            nullDescriptor.Should().Throw<ArgumentException>();
            var descriptors = new[] { RegisteredMessage.Descriptor, RegisteredMessage.Descriptor };
            var setup = ProtobufSerializerSetup.Create(descriptors);
            descriptors[0] = UnregisteredMessage.Descriptor;
            setup.MessageDescriptors.Should().ContainSingle().Which.Should().Be(RegisteredMessage.Descriptor);
        }

        private static async Task WithSwitchAsync(bool enabled, Func<Task> body)
        {
            const string switchName = "Akka.DynamicTypeLoading";
            var hadSwitch = AppContext.TryGetSwitch(switchName, out var previous);
            AppContext.SetSwitch(switchName, enabled);
            try
            {
                await body();
            }
            finally
            {
                AppContext.SetSwitch(switchName, !hadSwitch || previous);
            }
        }

        private sealed class ProtobufEchoActor : ReceiveActor
        {
            public ProtobufEchoActor()
            {
                Receive<RegisteredMessage>(message => Sender.Tell(message));
            }
        }
    }
}
