using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Serialization;
using Akka.TestKit.Xunit2;

namespace DocsExamples.Configuration
{
    // <Protocol>
    public interface IAppProtocol{}

    public sealed class Ack : IAppProtocol{ }

    public sealed class Nack : IAppProtocol{ }
    // </Protocol>

    // <Serializer>
    public sealed class AppProtocolSerializer : SerializerWithStringManifest
    {
        public AppProtocolSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override byte[] ToBinary(object obj)
        {
            switch (obj)
            {
                // no dynamic content here - manifest is enough to tell us what message type is
                // so no need to populate byte array
                case Ack _:
                    return Array.Empty<byte>();
                case Nack _:
                    return Array.Empty<byte>();
                default:
                    throw new NotImplementedException($"Unsupported serialization type [{obj.GetType()}]");
            }
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch (manifest)
            {
                case "A":
                    return new Ack();
                case "N":
                    return new Nack();
                default:
                    throw new NotImplementedException($"Unsupported serialization manifest [{manifest}]");
            }
        }

        public override string Manifest(object o)
        {
            switch (o)
            {
                case Ack _:
                    return "A";
                case Nack _:
                    return "N";
                default:
                    throw new NotImplementedException($"Unsupported serialization type [{o.GetType()}]");
            }
        }
    }
    // </Serializer>

    public class SerializationSetupDocSpec : TestKit
    {

    }
}
