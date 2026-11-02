using System;
using Akka.Actor;
using Akka.Serialization;
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
}