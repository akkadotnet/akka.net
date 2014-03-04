using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Serialization
{
    public class Information
    {
        public Address Address { get; set; }
        public ActorSystem System { get; set; }
    }
    public class Serialization
    {
        private Dictionary<int, Serializer> serializers = new Dictionary<int, Serializer>();
        private Serializer jsonSerializer;
        private Serializer javaSerializer;
        private Serializer nullSerializer;
        private Serializer byteArraySerializer;

        [ThreadStatic]
        public Information CurrentTransportInformation;

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

        public string SerializedActorPath(ActorRef @ref)
        {
            /*
val path = actorRef.path
    val originalSystem: ExtendedActorSystem = actorRef match {
      case a: ActorRefWithCell ⇒ a.underlying.system.asInstanceOf[ExtendedActorSystem]
      case _                   ⇒ null
    }
    Serialization.currentTransportInformation.value match {
      case null ⇒ originalSystem match {
        case null ⇒ path.toSerializationFormat
        case system ⇒
          try path.toSerializationFormatWithAddress(system.provider.getDefaultAddress)
          catch { case NonFatal(_) ⇒ path.toSerializationFormat }
      }
      case Information(address, system) ⇒
        if (originalSystem == null || originalSystem == system)
          path.toSerializationFormatWithAddress(address)
        else {
          val provider = originalSystem.provider
          path.toSerializationFormatWithAddress(provider.getExternalAddressFor(address).getOrElse(provider.getDefaultAddress))
        }
    }*/
            ActorSystem originalSystem = null;
            if (@ref is ActorRefWithCell)
            {
                originalSystem = @ref.AsInstanceOf<ActorRefWithCell>().Cell.System;
                return @ref.Path.ToStringWithAddress(CurrentTransportInformation.Address);
            }
            else
            {
                return @ref.Path.ToSerializationFormat();
            }            
        }
    }
}
