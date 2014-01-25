using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Serialization
{
    public abstract class Serializer
    {
        public abstract bool RequiresManifest { get; }
        public abstract byte[] ToBinary(object obj);
        public abstract object FromBinary(byte[] bytes, Type type);
    }

    public class JsonSerializer : Serializer
    {
        public override bool RequiresManifest
        {
            get { return false; }
        }
        public override object FromBinary(byte[] bytes, Type type)
        {
            var json = Encoding.Default.GetString(bytes);
            return fastJSON.JSON.Instance.ToObject(json);
        }

        public override byte[] ToBinary(object obj)
        {
            var json = fastJSON.JSON.Instance.ToJSON(obj);
            var bytes = Encoding.Default.GetBytes(json);
            return bytes;
        }
    }

    public class ProtoBufSerializer : Serializer
    {

        public override bool RequiresManifest
        {
            get { return true; }
        }

        public override byte[] ToBinary(object obj)
        {
            using(var stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream,obj);
                return stream.ToArray();
            }
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            using(var stream = new MemoryStream(bytes))
            {
                return ProtoBuf.Serializer.NonGeneric.Deserialize(type, stream);
            }
        }
    }
}
