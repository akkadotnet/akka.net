using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Serialization
{
    public class Serialization
    {
        private Dictionary<int, Serializer> serializers = new Dictionary<int, Serializer>();
        private Serializer jsonSerializer = new JsonSerializer();
        private Serializer javaSerializer = new JavaSerializer();
        private Serializer nullSerializer = new NullSerializer();
        private Serializer byteArraySerializer = new ByteArraySerializer();

        private Dictionary<Type, Serializer> serializerMap = new Dictionary<Type, Serializer>();


        public Serialization(ActorSystem system)
        {
            this.System = system;
            serializers.Add(jsonSerializer.Identifier, jsonSerializer);
            serializers.Add(javaSerializer.Identifier, javaSerializer);
            serializers.Add(nullSerializer.Identifier,nullSerializer);
            serializers.Add(byteArraySerializer.Identifier,byteArraySerializer);
            serializerMap.Add(typeof(object), jsonSerializer);
        }

        public object Deserialize(byte[] bytes,int serializerId,Type type)
        {
            return serializers[serializerId].FromBinary(bytes, type);
        }

        public Serializer FindSerializerFor(object obj)
        {
            if (obj == null)
                return nullSerializer;
            //if (obj is byte[])
            //    return byteArraySerializer;

            var type = obj.GetType();
            while(type != null)
            {
                if (serializerMap.ContainsKey(type))
                    return serializerMap[type];
                type = type.BaseType;
            }
            throw new Exception("Serializer not found for type " + obj.GetType().Name);
        }

        public ActorSystem System { get;private set; }
    }
}
