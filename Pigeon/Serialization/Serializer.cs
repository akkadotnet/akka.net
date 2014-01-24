using System;
using System.Collections.Generic;
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
}
