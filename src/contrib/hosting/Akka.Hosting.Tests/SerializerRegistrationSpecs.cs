#nullable enable
using System;
using Akka.Actor;
using Akka.Serialization;
using Akka.Serialization.V2;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Akka.Hosting.Tests
{
    public class SerializerRegistrationSpecs
    {
        public interface IUseCustomSerializer
        {
        }

        public sealed class CustomSerializedMessage : IUseCustomSerializer
        {
            public static readonly CustomSerializedMessage Instance = new CustomSerializedMessage();

            private CustomSerializedMessage()
            {
            }
        }

        public sealed class CustomSerializer : SerializerWithStringManifest
        {
            public CustomSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => 435;

            public override byte[] ToBinary(object obj)
            {
                throw new NotImplementedException();
            }

            public override object FromBinary(byte[] bytes, string manifest)
            {
                throw new NotImplementedException();
            }

            public override string Manifest(object o)
            {
                throw new NotImplementedException();
            }
        }

        [Fact(DisplayName = "Should_RegisterGeneratedBindings_When_ComposedWithCustomSerializer")]
        public void Should_RegisterGeneratedBindings_When_ComposedWithCustomSerializer()
        {
            var services = new ServiceCollection();
            services.AddAkka("GeneratedSerializerSys", builder =>
            {
                builder.WithCustomSerializer(HostingGeneratedSerializer.CreateRegistration())
                    .Should().BeSameAs(builder);
                builder.WithCustomSerializer("existing", new[] { typeof(IUseCustomSerializer) },
                    system => new CustomSerializer(system));
            });

            using var provider = services.BuildServiceProvider();
            using var system = provider.GetRequiredService<ActorSystem>();
            var serializer = system.Serialization.FindSerializerFor(new HostingGeneratedMessage());
            serializer.Should().BeOfType<HostingGeneratedSerializer>();
            serializer.Identifier.Should().Be(120001);
            system.Serialization.FindSerializerFor(new HostingAdditionalMessage()).Should().BeSameAs(serializer);
            system.Serialization.FindSerializerFor(CustomSerializedMessage.Instance).Should().BeOfType<CustomSerializer>();
        }

        [Fact]
        public void ShouldAddCustomSerializer()
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddAkka("TestSys", (builder, provider) =>
            {
                builder.WithCustomSerializer("my-serializer", new[] { typeof(IUseCustomSerializer) },
                    system => new CustomSerializer(system));
            });
            using var sp = serviceCollection.BuildServiceProvider();
            using var actorSystem = sp.GetRequiredService<ActorSystem>();
            var serializer = actorSystem.Serialization.FindSerializerFor(CustomSerializedMessage.Instance);
            serializer.Should().BeOfType<CustomSerializer>();
        }
    }

    public interface IHostingGeneratedMessage;

    [AkkaSerializable(Manifest = "generated", AllowEmpty = true)]
    public sealed class HostingGeneratedMessage : IHostingGeneratedMessage;

    [AkkaSerializable(Manifest = "additional", AllowEmpty = true)]
    public sealed class HostingAdditionalMessage;

    [AkkaSerializer<IHostingGeneratedMessage>("hosting-generated", 120001)]
    [AkkaSerializable<HostingAdditionalMessage>(Manifest = "additional")]
    public sealed partial class HostingGeneratedSerializer : AkkaSerializer
    {
        public static partial Akka.Serialization.V2.SerializerRegistration CreateRegistration();
    }
}
