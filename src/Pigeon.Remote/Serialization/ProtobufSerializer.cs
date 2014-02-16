using Pigeon.Actor;
using Pigeon.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote.Serialization
{
    public class ProtobufSerializer : Serializer
    {
        public ProtobufSerializer(ActorSystem system) : base(system) { }

        public override bool IncludeManifest
        {
            get { return true; }
        }

        public override byte[] ToBinary(object obj)
        {
            using (var stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, obj);
                return stream.ToArray();
            }
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            using (var stream = new MemoryStream(bytes))
            {
                return ProtoBuf.Serializer.NonGeneric.Deserialize(type, stream);
            }
        }

        public override int Identifier
        {
            get { return 2; }
        }
    }
}
