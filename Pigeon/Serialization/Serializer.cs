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
        public abstract int Identifier { get; }
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

        public override int Identifier
        {
            get { return -1; }
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

        public override int Identifier
        {
            get { return 2; }
        }
    }

    public class NullSerializer : Serializer
    {
        private readonly byte[] nullBytes = { };
        public override int Identifier
        {
            get { return 0; }
        }

        public override bool RequiresManifest
        {
            get { return false; }
        }

        public override byte[] ToBinary(object obj)
        {
            return nullBytes;
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            return null;
        }
    }

    public class ByteArraySerializer : Serializer
    {
        public override int Identifier
        {
            get { return 4; }
        }

        public override bool RequiresManifest
        {
            get { return false; }
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj == null)
                return null;
            if (obj is byte[])
                return (byte[])obj;            
            throw new NotSupportedException();
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            return bytes;
        }
    }
}
