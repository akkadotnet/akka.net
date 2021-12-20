using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Google.Protobuf;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests.Serialization
{
    public class BugFix5062Spec: AkkaSpec
    {
        private static Config DDataConfig => ConfigurationFactory.ParseString($@"
            akka.actor {{
                serializers {{
                    dummyWithManifest = ""{typeof(DummySerializerWithStringManifest).AssemblyQualifiedName}""
                }}
                serialization-bindings {{
                    ""{typeof(SomeMessage).AssemblyQualifiedName}"" = dummyWithManifest
                }}
                serialization-identifiers {{
	                ""{typeof(DummySerializerWithStringManifest).AssemblyQualifiedName}"" = 13
                }}
            }}")
            .WithFallback(RemoteConfigFactory.Default());

        public BugFix5062Spec(ITestOutputHelper output) : base(output, DDataConfig)
        { }

        [Fact]
        public void Failed_serialization_should_give_proper_exception_message()
        {
            var childName = "dummy";
            var message = new ActorSelectionMessage(
                new SomeMessage(), 
                new SelectionPathElement[] { new SelectChildName(childName) }, 
                true);

            var node1 = new Address("akka.tcp", "Sys", "localhost", 2551);
            var serialized = MessageSerializer.Serialize((ExtendedActorSystem)Sys, node1, message);

            var o = new object();
            o.Invoking(s => MessageSerializer.Deserialize((ExtendedActorSystem)Sys, serialized)).Should()
                .Throw<SerializationException>()
                .WithMessage($"Failed to deserialize payload object when deserializing {nameof(ActorSelectionMessage)} with payload [SerializerId=13, Manifest=SM] addressed to [{childName}]. {Serializer.GetErrorForSerializerId(13)}")
                .WithInnerExceptionExactly<NotImplementedException>();
        }

        public class SomeMessage
        {
        }

        public class DummySerializerWithStringManifest : SerializerWithStringManifest
        {
            public DummySerializerWithStringManifest(ExtendedActorSystem system) : base(system)
            {
            }

            public override byte[] ToBinary(object obj)
            {
                return Array.Empty<byte>();
            }

            public override object FromBinary(byte[] bytes, string manifest)
            {
                throw new NotImplementedException();
            }

            public override string Manifest(object o)
            {
                if (o is SomeMessage)
                    return "SM";
                throw new Exception("Unknown object type");
            }
        }


    }
}
