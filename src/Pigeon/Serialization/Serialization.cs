using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Serialization
{
    public class Serialization
    {
        private Dictionary<int, Serializer> serializers = new Dictionary<int, Serializer>();
        private Serializer jsonSerializer;
        private Serializer javaSerializer;
        private Serializer nullSerializer;
        private Serializer byteArraySerializer;

        private Dictionary<Type, Serializer> serializerMap = new Dictionary<Type, Serializer>();


        public Serialization(ActorSystem system)
        {
            this.System = system;
            jsonSerializer = new JsonSerializer(system);
            javaSerializer = new JavaSerializer(system);
            nullSerializer = new NullSerializer(system);
            byteArraySerializer = new ByteArraySerializer(system);

            serializers.Add(jsonSerializer.Identifier, jsonSerializer);
            serializers.Add(javaSerializer.Identifier, javaSerializer);
            serializers.Add(nullSerializer.Identifier,nullSerializer);
            serializers.Add(byteArraySerializer.Identifier,byteArraySerializer);
            serializerMap.Add(typeof(object), jsonSerializer);
        }

        public void AddSerializer(Serializer serializer)
        {
            this.serializers.Add(serializer.Identifier,serializer);
        }
        public void AddSerializationMap(Type type, Serializer serializer)
        {
            this.serializerMap.Add(type, serializer);
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
            return FindSerializerForType(type);
        }

        public Serializer FindSerializerForType(Type objectType)
        {
            var type = objectType;
            while (type != null)
            {
                if (serializerMap.ContainsKey(type))
                    return serializerMap[type];
                type = type.BaseType;
            }
            throw new Exception("Serializer not found for type " + objectType.Name);
        }

        public ActorSystem System { get;private set; }
    }
}
