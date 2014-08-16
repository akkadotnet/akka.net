using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Serialization
{
    public class Information
    {
        public Address Address { get; set; }
        public ActorSystem System { get; set; }
    }

    public class Serialization
    {
        [ThreadStatic] public static Information CurrentTransportInformation;

        [ThreadStatic] public static ActorSystem CurrentSystem;
        private readonly Serializer nullSerializer;

        private readonly Dictionary<Type, Serializer> serializerMap = new Dictionary<Type, Serializer>();
        private readonly Dictionary<int, Serializer> serializers = new Dictionary<int, Serializer>();
        private Serializer byteArraySerializer;
        private Serializer javaSerializer;
        private Serializer newtonsoftJsonSerializer;


        public Serialization(ExtendedActorSystem system)
        {
            System = system;
            newtonsoftJsonSerializer = new NewtonSoftJsonSerializer(system);
            //protobufnetSerializer = new ProtoBufNetSerializer(system);
            //jsonSerializer = new FastJsonSerializer(system);
            javaSerializer = new JavaSerializer(system);
            nullSerializer = new NullSerializer(system);
            byteArraySerializer = new ByteArraySerializer(system);

            serializers.Add(newtonsoftJsonSerializer.Identifier, newtonsoftJsonSerializer);
            //serializers.Add(protobufnetSerializer.Identifier, protobufnetSerializer);
            //serializers.Add(jsonSerializer.Identifier, jsonSerializer);
            serializers.Add(javaSerializer.Identifier, javaSerializer);
            serializers.Add(nullSerializer.Identifier, nullSerializer);
            serializers.Add(byteArraySerializer.Identifier, byteArraySerializer);

            serializerMap.Add(typeof (object), newtonsoftJsonSerializer);
        }

        public ActorSystem System { get; private set; }

        public void AddSerializer(Serializer serializer)
        {
            serializers.Add(serializer.Identifier, serializer);
        }

        public void AddSerializationMap(Type type, Serializer serializer)
        {
            serializerMap.Add(type, serializer);
        }

        public object Deserialize(byte[] bytes, int serializerId, Type type)
        {
            return serializers[serializerId].FromBinary(bytes, type);
        }

        public Serializer FindSerializerFor(object obj)
        {
            if (obj == null)
                return nullSerializer;
            //if (obj is byte[])
            //    return byteArraySerializer;

            Type type = obj.GetType();
            return FindSerializerForType(type);
        }

        public Serializer FindSerializerForType(Type objectType)
        {
            Type type = objectType;
            while (type != null)
            {
                if (serializerMap.ContainsKey(type))
                    return serializerMap[type];
                type = type.BaseType;
            }
            throw new Exception("Serializer not found for type " + objectType.Name);
        }

        public static string SerializedActorPath(ActorRef @ref)
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
                originalSystem = @ref.AsInstanceOf<ActorRefWithCell>().Underlying.System;
                if (CurrentTransportInformation == null)
                {
                    return @ref.Path.ToSerializationFormat();
                }
                return @ref.Path.ToStringWithAddress(CurrentTransportInformation.Address);
            }
            return @ref.Path.ToSerializationFormat();
        }

        public Serializer GetSerializerById(int serializerId)
        {
            return serializers[serializerId];
        }
    }
}